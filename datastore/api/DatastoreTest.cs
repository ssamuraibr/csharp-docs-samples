﻿// Copyright 2016 Google Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
using System;
using Google.Datastore.V1Beta3;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace GoogleCloudSamples
{
    [TestClass]
    public class DatastoreTest
    {
        private readonly string _projectId;
        private readonly DatastoreDb _db;
        private readonly Entity _sampleTask;
        private readonly KeyFactory _keyFactory;
        private readonly DateTime _includedDate = new DateTime(1999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        public DatastoreTest()
        {
            _projectId = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID");
            _db = DatastoreDb.Create(_projectId);
            _keyFactory = _db.CreateKeyFactory("Task");
            _sampleTask = new Entity()
            {
                Key = _keyFactory.CreateKey("sampleTask"),
            };
        }

        private bool IsValidKey(Key key)
        {
            foreach (var element in key.Path)
            {
                if (element.Id == 0 && string.IsNullOrEmpty(element.Name))
                    return false;
                if (string.IsNullOrEmpty(element.Kind))
                    return false;
            }
            return true;
        }

        [TestMethod]
        public void TestIncompleteKey()
        {
            // [START incomplete_key]
            Key incompleteKey = _db.CreateKeyFactory("Task").CreateIncompleteKey();
            Key key = _db.AllocateId(incompleteKey);
            // [END incomplete_key]
            Assert.IsTrue(IsValidKey(key));
        }

        [TestMethod]
        public void TestNamedKey()
        {
            // [START named_key]
            Key key = _db.CreateKeyFactory("Task").CreateKey("sampleTask");
            // [END named_key]
            Assert.IsTrue(IsValidKey(key));
        }

        [TestMethod]
        public void TestKeyWithParent()
        {
            // [START key_with_parent]
            Key rootKey = _db.CreateKeyFactory("TaskList").CreateKey("default");
            Key key = new KeyFactory(rootKey, "Task").CreateKey("sampleTask");
            // [END key_with_parent]
            Assert.IsTrue(IsValidKey(key));
        }

        [TestMethod]
        public void TestKeyWithMultilevelParent()
        {
            // [START key_with_multilevel_parent]
            Key rootKey = _db.CreateKeyFactory("User").CreateKey("Alice");
            Key taskListKey = new KeyFactory(rootKey, "TaskList").CreateKey("default");
            Key key = new KeyFactory(taskListKey, "Task").CreateKey("sampleTask");
            // [END key_with_multilevel_parent]
            Assert.IsTrue(IsValidKey(key));
        }

        private void AssertValidEntity(Entity original)
        {
            _db.Upsert(original);
            Assert.AreEqual(original, _db.Lookup(original.Key));
        }

        [TestMethod]
        public void TestEntityWithParent()
        {
            // [START entity_with_parent]
            Key taskListKey = _db.CreateKeyFactory("TaskList").CreateKey("default");
            Key taskKey = new KeyFactory(taskListKey, "Task").CreateKey("sampleTask");
            Entity task = new Entity()
            {
                Key = taskKey,
                ["type"] = "Personal",
                ["done"] = false,
                ["priority"] = 4,
                ["description"] = "Learn Cloud Datastore"
            };
            // [END entity_with_parent]
            AssertValidEntity(task);
        }

        [TestMethod]
        public void TestProperties()
        {
            // [START properties]
            Entity task = new Entity()
            {
                Key = _db.CreateKeyFactory("Task").CreateKey("sampleTask"),
                ["type"] = "Personal",
                ["created"] = new DateTime(1999, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                ["done"] = false,
                ["priority"] = 4,
                ["percent_complete"] = 10.0,
            };
            (task["description"] = "Learn Cloud Datastore").ExcludeFromIndexes = true;
            // [END properties]
            AssertValidEntity(task);
        }

        [TestMethod]
        public void TestArrayValue()
        {
            // [START array_value]
            Entity task = new Entity()
            {
                Key = _db.CreateKeyFactory("Task").CreateKey("sampleTask"),
                ["tags"] = new ArrayValue(),
                ["collaborators"] = new ArrayValue()
            };
            var tags = task["tags"].ArrayValue.Values;
            tags.Add("fun");
            tags.Add("programming");
            var collaborators = task["collaborators"].ArrayValue.Values;
            collaborators.Add("alice");
            collaborators.Add("bob");
            // [END array_value]
            AssertValidEntity(task);
        }

        [TestMethod]
        public void TestBasicEntity()
        {
            // [START basic_entity]
            Entity task = new Entity()
            {
                Key = _db.CreateKeyFactory("Task").CreateKey("sampleTask"),
                ["type"] = "Personal",
                ["done"] = false,
                ["priority"] = 4,
                ["description"] = "Learn Cloud Datastore"
            };
            // [END basic_entity]
            AssertValidEntity(task);
        }

        [TestMethod]
        public void TestUpsert()
        {
            // [START upsert]
            _db.Upsert(_sampleTask);
            // [END upsert]
            Assert.AreEqual(_sampleTask, _db.Lookup(_sampleTask.Key));
            // Make sure a second upsert doesn't throw an exception.
            _db.Upsert(_sampleTask);
        }

        [TestMethod]
        public void TestInsert()
        {
            // [START insert]
            Entity task = new Entity()
            {
                Key = _keyFactory.CreateIncompleteKey()
            };
            task.Key = _db.Insert(task);
            // [END insert]
            Assert.AreEqual(task, _db.Lookup(task.Key));
            // Make sure a second insert throws an exception.
            try
            {
                _db.Insert(task);
                Assert.Fail("_db.Insert should throw an exception because an " +
                    $"entity with the key {task.Key} already exits.");
            }
            catch (Grpc.Core.RpcException e)
            {
                Assert.AreEqual(Grpc.Core.StatusCode.InvalidArgument, e.Status.StatusCode);
            }
        }

        [TestMethod]
        public void TestLookup()
        {
            _db.Upsert(_sampleTask);
            // [START lookup]
            Entity task = _db.Lookup(_sampleTask.Key);
            // [END lookup]
            Assert.AreEqual(_sampleTask, task);
        }


        [TestMethod]
        public void TestUpdate()
        {
            _db.Upsert(_sampleTask);
            // [START update]
            _sampleTask["priority"] = 5;
            _db.Update(_sampleTask);
            // [END update]
            Assert.AreEqual(_sampleTask, _db.Lookup(_sampleTask.Key));
        }

        [TestMethod]
        public void TestDelete()
        {
            _db.Upsert(_sampleTask);
            // [START delete]
            _db.Delete(_sampleTask.Key);
            // [END delete]
            Assert.IsNull(_db.Lookup(_sampleTask.Key));
        }

        private Entity[] UpsertBatch(Key taskKey1, Key taskKey2)
        {
            var taskList = new[]
            {
                new Entity()
                {
                    Key = taskKey1,
                    ["type"] = "Personal",
                    ["done"] = false,
                    ["priority"] = 4,
                    ["description"] = "Learn Cloud Datastore"
                },
                new Entity()
                {
                    Key = taskKey2,
                    ["type"] = "Personal",
                    ["done"] = "false",
                    ["priority"] = 5,
                    ["description"] = "Integrate Cloud Datastore"
                }
            };
            _db.Upsert(taskList);
            return taskList;
        }

        [TestMethod]
        public void TestBatchUpsert()
        {
            // [START batch_upsert]
            var taskList = new[]
            {
                new Entity()
                {
                    Key = _keyFactory.CreateIncompleteKey(),
                    ["type"] = "Personal",
                    ["done"] = false,
                    ["priority"] = 4,
                    ["description"] = "Learn Cloud Datastore"
                },
                new Entity()
                {
                    Key = _keyFactory.CreateIncompleteKey(),
                    ["type"] = "Personal",
                    ["done"] = "false",
                    ["priority"] = 5,
                    ["description"] = "Integrate Cloud Datastore"
                }
            };
            var keyList =_db.Upsert(taskList);
            // [END batch_upsert]
            taskList[0].Key = keyList[0];
            taskList[1].Key = keyList[1];
            Assert.AreEqual(taskList[0], _db.Lookup(keyList[0]));
            Assert.AreEqual(taskList[1], _db.Lookup(keyList[1]));
        }

        [TestMethod]
        public void TestBatchLookup()
        {
            // [START batch_lookup]
            var keys = new Key[] { _keyFactory.CreateKey(1), _keyFactory.CreateKey(2) };
            // [END batch_lookup]
            var expectedTasks = UpsertBatch(keys[0], keys[1]);
            // [START batch_lookup]
            var tasks = _db.Lookup(keys);
            // [END batch_lookup]
            Assert.AreEqual(expectedTasks[0], tasks[0]);
            Assert.AreEqual(expectedTasks[1], tasks[1]);
        }

        [TestMethod]
        public void TestBatchDelete()
        {
            // [START batch_delete]
            var keys = new Key[] { _keyFactory.CreateKey(1), _keyFactory.CreateKey(2) };
            // [END batch_delete]
            UpsertBatch(keys[0], keys[1]);
            var lookups = _db.Lookup(keys);
            Assert.IsNotNull(lookups[0]);
            Assert.IsNotNull(lookups[1]);
            // [START batch_delete]
            _db.Delete(keys);
            // [END batch_delete]
            lookups = _db.Lookup(keys);
            Assert.IsNull(lookups[0]);
            Assert.IsNull(lookups[1]);
        }

        private void UpsertTaskList()
        {
            Key taskListKey = _db.CreateKeyFactory("TaskList").CreateKey("default");
            Key taskKey = new KeyFactory(taskListKey, "Task").CreateKey("someTask");
            Entity task = new Entity()
            {
                Key = taskKey,
                ["type"] = "Personal",
                ["done"] = false,
                ["completed"] = false,
                ["priority"] = 4,
                ["created"] = _includedDate,
                ["percent_complete"] = 10.0,
                ["description"] = "Learn Cloud Datastore",
                ["tag"] = new ArrayValue()
            };
            task["description"].ExcludeFromIndexes = true;
            foreach (var tag in new[] { "fun", "l", "programming" })
                task["tag"].ArrayValue.Values.Add(tag);
            _db.Upsert(task);
        }

        [TestMethod]
        public void TestBasicQuery()
        {
            UpsertTaskList();
            // [START basic_query]
            Query query = new Query("Task")
            {
                Filter = new Filter()
                {
                    CompositeFilter = new CompositeFilter()
                    {
                        Op = CompositeFilter.Types.Operator.And
                    }
                },
            };
            query.Filter.CompositeFilter.Filters.Add(new Filter()
            {
                PropertyFilter = new PropertyFilter()
                {
                    Op = PropertyFilter.Types.Operator.Equal,
                    Property = new PropertyReference("done"),
                    Value = false,
                }
            });
            query.Filter.CompositeFilter.Filters.Add(new Filter()
            {
                PropertyFilter = new PropertyFilter()
                {
                    Op = PropertyFilter.Types.Operator.GreaterThanOrEqual,
                    Property = new PropertyReference("priority"),
                    Value = 4,
                }
            });
            query.Order.Add(new PropertyOrder()
            {
                Property = new PropertyReference("priority")
            });
            // [END basic_query]
            Assert.AreEqual(1,_db.RunQuery(query).Count());
        }
    }
}