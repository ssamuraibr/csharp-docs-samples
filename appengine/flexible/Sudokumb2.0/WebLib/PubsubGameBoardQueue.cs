using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Sudokumb
{
    public class PubsubGameBoardQueueOptions
    {
        /// <summary>
        /// The Google Cloud project id.
        /// </summary>
        public string ProjectId { get; set; }
        /// <summary>
        /// The Pub/sub subscription from which solve messages are read.
        /// </summary>
        public string SubscriptionId { get; set; } = "sudokumb4";
        /// <summary>
        /// The Pub/sub topic where solve messages are written.
        /// </summary>
        public string TopicId { get; set; } = "sudokumb4";
        /// <summary>
        /// When exploring the game tree, the number of branches that should
        /// be explored in parallel.
        /// </summary>
        /// <returns></returns>
        public int MaxParallelBranches { get; set; } = 10;
    }

    public static class PubsubGameBoardQueueExtensions
    {
        public static IServiceCollection AddPubsubGameBoardQueueAndSolver(
            this IServiceCollection services)
        {
            services.AddSingleton<PubsubGameBoardQueue>();
            services.AddSingleton<IGameBoardQueue, PubsubGameBoardQueue>(
                provider => provider.GetService<PubsubGameBoardQueue>()
            );
            services.AddSingleton<IHostedService, PubsubGameBoardQueue>(
                provider => provider.GetService<PubsubGameBoardQueue>()
            );
            return services;
        }
    }

    // Implements a GameBoardQueue using Pub/sub.  The next boards to be
    // evaluated are published to a Pub/sub topic.
    public class PubsubGameBoardQueueImpl
    {
        readonly PublisherServiceApiClient _publisherApi;
        readonly PublisherClient _publisherClient;
        readonly SubscriberClient _subscriberClient;
        readonly ILogger<PubsubGameBoardQueueImpl> _logger;
        private readonly SolveStateStore _solveStateStore;
        readonly IOptions<PubsubGameBoardQueueOptions> _options;
        readonly Solver _solver;

        public PubsubGameBoardQueueImpl(
            IOptions<PubsubGameBoardQueueOptions> options,
            ILogger<PubsubGameBoardQueueImpl> logger,
            SolveStateStore solveStateStore,
            Solver solver)
        {
            _logger = logger;
            _solveStateStore = solveStateStore;
            _options = options;
            _solver = solver;
            _publisherApi = PublisherServiceApiClient.Create();
            var subscriberApi = SubscriberServiceApiClient.Create();
            _publisherClient = PublisherClient.Create(MyTopic,
                new [] { _publisherApi});
            _subscriberClient = SubscriberClient.Create(MySubscription,
                new [] {subscriberApi}, new SubscriberClient.Settings()
                {
                    StreamAckDeadline = TimeSpan.FromMinutes(1)
                });

            // Create the Topic and Subscription.
            try
            {
                _publisherApi.CreateTopic(MyTopic);
                _logger.LogInformation("Created {0}.", MyTopic.ToString());
            }
            catch (RpcException e)
            when (e.Status.StatusCode == StatusCode.AlreadyExists)
            {
                // Already exists.  That's fine.
            }

            try
            {
                subscriberApi.CreateSubscription(MySubscription, MyTopic,
                    pushConfig: null, ackDeadlineSeconds: 10);
                _logger.LogInformation("Created {0}.",
                    MySubscription.ToString());
            }
            catch (RpcException e)
            when (e.Status.StatusCode == StatusCode.AlreadyExists)
            {
                // Already exists.  That's fine.
            }
        }

        public TopicName MyTopic
        {
             get
             {
                 var opts = _options.Value;
                 return new TopicName(opts.ProjectId, opts.TopicId);
             }
        }

        public SubscriptionName MySubscription
        {
             get
             {
                 var opts = _options.Value;
                 return new SubscriptionName(opts.ProjectId,
                    opts.SubscriptionId);
             }
        }

        public async Task<bool> Publish(string solveRequestId,
            IEnumerable<GameBoard> gameBoards, int gameSearchTreeDepth,
            CancellationToken cancellationToken)
        {
            var messages = gameBoards.Select(board => new GameBoardMessage()
            {
                SolveRequestId = solveRequestId,
                Stack = new [] {new BoardAndWidth { Board = board, ParallelBranches = 1} },
            });
            var pubsubMessages = messages.Select(message => new PubsubMessage()
            {
                Data = ByteString.CopyFromUtf8(JsonConvert.SerializeObject(
                    message))
            });
            await _publisherApi.PublishAsync(MyTopic, pubsubMessages,
                CallSettings.FromCancellationToken(cancellationToken));
            return false;
        }

        /// <summary>
        /// Solve one sudoku puzzle.
        /// </summary>
        /// <param name="pubsubMessage">The message as it arrived from Pub/Sub.
        /// </param>
        /// <returns>Ack or Nack</returns>
        async Task<SubscriberClient.Reply> ProcessOneMessage(
            PubsubMessage pubsubMessage, CancellationToken cancellationToken)
        {
            // Unpack the pubsub message.
            string text = pubsubMessage.Data.ToString(Encoding.UTF8);
            GameBoardMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<GameBoardMessage>(text);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Bad message in subscription {0}\n{1}",
                    MySubscription, text);
                return SubscriberClient.Reply.Ack;
            }
            if (message.Stack == null || message.Stack.Length == 0 ||
                string.IsNullOrEmpty(message.SolveRequestId))
            {
                _logger.LogError("Bad message in subscription {0}\n{1}",
                    MySubscription, text);
                return SubscriberClient.Reply.Ack;
            }
            // Examine the board.
            IEnumerable<GameBoard> nextMoves;
            _solveStateStore.IncreaseExaminedBoardCount(
                message.SolveRequestId, 1);
            BoardAndWidth top = message.Stack.Last();
            if (_solver.ExamineGameBoard(top.Board, out nextMoves))
            {
                // Yay!  Solved the game.
                await _solveStateStore.SetAsync(message.SolveRequestId,
                    top.Board, cancellationToken);
                return SubscriberClient.Reply.Ack;
            }
            // Explore the next possible moves.
            List<Task> tasks = new List<Task>();
            List<GameBoard> stackMoves = new List<GameBoard>();
            int parallelBranches = top.ParallelBranches.GetValueOrDefault(
                _options.Value.MaxParallelBranches);
            int nextLevelWidth = (1 + nextMoves.Count()) * parallelBranches;
            if (nextLevelWidth > _options.Value.MaxParallelBranches) 
            {
                // Too many branches already.  Explore this branch linearly.            
                List<BoardAndWidth> stack =
                    new List<BoardAndWidth>(message.Stack.SkipLast(1));
                stack.AddRange(nextMoves.Select(move => new BoardAndWidth 
                { 
                    Board = move, 
                    ParallelBranches = top.ParallelBranches
                }));
                message.Stack = stack.ToArray();
                // Republish the message with the new stack.
                string newText = JsonConvert.SerializeObject(message);
                tasks.Add(_publisherClient.PublishAsync(new PubsubMessage()
                {
                    Data = ByteString.CopyFromUtf8(newText)
                }));
            }
            else
            {
                // Branch out.
                top.ParallelBranches = nextLevelWidth;
                foreach (GameBoard move in nextMoves)
                {
                    top.Board = move;
                    // Republish the message with the new stack.
                    string newText = JsonConvert.SerializeObject(message);
                    tasks.Add(_publisherClient.PublishAsync(new PubsubMessage()
                    {
                        Data = ByteString.CopyFromUtf8(newText)
                    }));
                }
                if (message.Stack.Length > 1)
                {
                    // Pop the top.
                    message.Stack = message.Stack.SkipLast(1).ToArray();
                    // Republish the message with the new stack.
                    string newText = JsonConvert.SerializeObject(message);
                    tasks.Add(_publisherClient.PublishAsync(new PubsubMessage()
                    {
                        Data = ByteString.CopyFromUtf8(newText)
                    }));
                }
            }
            foreach (Task task in tasks) await task;
            return SubscriberClient.Reply.Ack;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Task.Run(async() =>
            {
                // This potentially hammers the CPU, so wait until everything
                // else starts up.
                await Task.Delay(TimeSpan.FromSeconds(10));
                await _subscriberClient.StartAsync(
                    (message, token) => ProcessOneMessage(message, token));
            });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            _subscriberClient.StopAsync(cancellationToken);
    }

    public class PubsubGameBoardQueue : PubsubGameBoardQueueImpl, IGameBoardQueue, IHostedService
    {
        public PubsubGameBoardQueue(
            IOptions<PubsubGameBoardQueueOptions> options,
            ILogger<PubsubGameBoardQueueImpl> logger,
            SolveStateStore solveStateStore, Solver solver)
            : base(options, logger, solveStateStore, solver)
        {
        }
    }

    class BoardAndWidth
    {
        public GameBoard Board { get; set; }
        public int? ParallelBranches { get; set; }
    }

    class GameBoardMessage
    {
        public string SolveRequestId { get; set; }
        public BoardAndWidth[] Stack { get; set; }
    }
}


