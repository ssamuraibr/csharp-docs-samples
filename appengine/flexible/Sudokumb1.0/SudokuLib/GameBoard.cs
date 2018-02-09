﻿/*
 * Copyright (c) 2017 Google Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy of
 * the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SudokuLib
{
    /// <summary>
    /// Represents a valid Sudoku gameboard.
    /// </summary>
    public class GameBoard
    {
        // Legal characters that can appear in _board.
        private static string _legalCharacters = "123456789 ";
        // An empty game board.  The initial state.
        private static string _blankBoard = new string(' ', 81);
        // A group is one of the 9 3x3 regions in the sudoku board.
        private static int[,] s_groupCenters = new int[9, 2]
        {
            {1, 1}, {1, 4}, {1, 8},
            {4, 1}, {4, 4}, {4, 8},
            {8, 1}, {8, 4}, {8, 8}
        };

        private string _board = _blankBoard;

        /// <summary>
        /// The Sudoku game board is represented as an 81-character long string.
        /// The first 9 characters are row 1.  The next 9 are row 2, etc.
        /// The only acceptable characters are 1-9 and space.
        /// 
        /// If you try to set Board to an invalid state, it will throw an 
        /// exception.
        /// </summary>
        public string Board
        {
            get { return _board; }
            set
            {
                // Validate the board.
                if (value.Length != 81)
                {
                    throw new ArgumentException("value", "String must be 81 characters.");
                }
                foreach (char c in value)
                {
                    if (_legalCharacters.IndexOf(c) < 0)
                        throw new ArgumentException("value", $"Illegal character: {c}");
                }
                for (int i = 0; i < 9; ++i)
                {
                    if (!IsLegal(GetRow(i, value)))
                        throw new ArgumentException("value", $"Row {i} contains duplicates: {GetRow(i, value)}");
                    if (!IsLegal(GetColumn(i, value)))
                        throw new ArgumentException("value", $"Column {i} contains duplicates: {GetColumn(i ,value)}");
                    int row = s_groupCenters[i, 0];
                    int col = s_groupCenters[i, 1];
                    if (!IsLegal(GetGroup(row, col, value)))
                        throw new ArgumentException("value", 
                            $"Group at row {row} column {col} contains duplicates: {GetGroup(row, col, value)}");
                }
                _board = value;
            }
        }

        public static GameBoard Create(string board) => new GameBoard()
        {
            Board = new string(board.Where((c) =>
                LegalCharacters.Contains(c)).ToArray())
        };

        /// <summary>
        /// The set of characters that can appear in a valid game board.
        /// </summary>
        public static string LegalCharacters { get { return _legalCharacters; } }

        /// <summary>
        /// Returns the elements in the row specified by zero-indexed rowNumber.
        /// </summary>
        /// <param name="rowNumber">Must be in the set [0,9).</param>
        public string Row(int rowNumber) => GetRow(rowNumber, _board);

        private static string GetRow(int rowNumber, string board)
        {
            Debug.Assert(rowNumber >= 0 && rowNumber < 9);
            return board.Substring(9 * rowNumber, 9);
        }

        /// <summary>
        /// Returns the elements in the column specified by zero-indexed colNumber.
        /// </summary>
        /// <param name="colNumber">Must be in the set [0,9)</param>
        public string Column(int colNumber) => GetColumn(colNumber, _board);

        private static string GetColumn(int colNumber, string board)
        {
            Debug.Assert(colNumber >= 0 && colNumber < 9);
            char[] column = new char[9];
            for (int i = 0; i < 9; ++i)
            {
                column[i] = board[colNumber + (i * 9)];
            }
            return new string(column);
        }

        /// <summary>
        /// Returns the elements in the group specified by zero-indexed
        /// rowNumber and groupNumber.
        /// </summary>
        /// <param name="rowNumber">Must be in the set [0,9).</param>
        /// <param name="colNumber">Must be in the set [0,9).</param>
        /// <returns></returns>
        public string Group(int rowNumber, int colNumber)
            => GetGroup(rowNumber, colNumber, _board);

        private static string GetGroup(int rowNumber, int colNumber, string board)
        {
            Debug.Assert(colNumber >= 0 && colNumber < 9);
            Debug.Assert(rowNumber >= 0 && rowNumber < 9);
            int start = (rowNumber - (rowNumber % 3)) * 9 +
                colNumber - (colNumber % 3);
            return board.Substring(start, 3)
                + board.Substring(start + 9, 3)
                + board.Substring(start + 18, 3);
        }

        /// <summary>
        /// Finds the next legal moves.
        /// </summary>
        /// <returns>A list of GameBoards with one fewer empty cell.
        /// </returns>
        public IEnumerable<GameBoard> FillNextEmptyCell()
        {
            var nextGameBoards = new List<GameBoard>();
            int i = _board.IndexOf(' ');
            if (i > 0)
            {
                int rowNumber = i / 9;
                int colNumber = i % 9;
                char[] board = _board.ToCharArray();
                foreach (char move in GetLegalMoves(rowNumber, colNumber))
                {
                    board[i] = move;
                    var g = new GameBoard();
                    g._board = new string(board);
                    nextGameBoards.Add(g);
                }
            }
            return nextGameBoards;
        }

        /// <summary>
        /// Returns true if the game is not complete.
        /// </summary>
        public bool HasEmptyCell() => _board.IndexOf(' ') >= 0;

        /// <summary>
        /// Calculates which numbers may be legally placed at rowNumber,
        /// colNumber.
        /// </summary>
        /// <returns>A list of characters that can be legally placed.</returns>
        private IEnumerable<char> GetLegalMoves(int rowNumber, int colNumber) =>
            "123456789".Except(Row(rowNumber).Union(Column(colNumber))
                .Union(Group(rowNumber, colNumber)));

        public override string ToString() => _board;

        /// <summary>
        /// Confirm that a row, column, or group is legal because it contains
        /// no duplicate numbers.
        /// </summary>
        /// <param name="group"></param>
        private static bool IsLegal(string group)
        {
            var withoutSpaces = group.Where((c) => c != ' ');
            return withoutSpaces.Count() == withoutSpaces.Distinct().Count();
        }

        public string ToPrettyString()
        {
            var s = new StringBuilder();
            for (int i = 0; i < Board.Length;  i += 9)
            {
                s.AppendFormat("{0}|{1}|{2}\n", _board.Substring(i, 3),
                    _board.Substring(i + 3, 3), _board.Substring(i + 6, 3));
                if (i == 18 || i == 45)
                    s.AppendLine("---+---+---");
            }
            return s.ToString();
        }
    }
}