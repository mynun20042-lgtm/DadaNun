// QrCodeGenerator.cs
// Self-contained, pure-C# QR Code generator (no external packages, no AI).
//
// This is a faithful implementation of the QR Code specification (ISO/IEC 18004).
// The algorithm follows the well-known, MIT-licensed reference design by Project Nayuki
// ("QR Code generator library"). See https://www.nayuki.io/page/qr-code-generator-library
//
// Reference license (MIT):
//   Copyright (c) Project Nayuki. (MIT License)
//   Permission is hereby granted, free of charge, to any person obtaining a copy of
//   this software and associated documentation files (the "Software"), to deal in the
//   Software without restriction, including without limitation the rights to use, copy,
//   modify, merge, publish, distribute, sublicense, and/or sell copies of the Software.
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Pure-algorithmic QR Code generator. Encodes text (byte mode, ECC level MEDIUM,
    /// automatic version selection) into a module matrix and can render it to a Texture2D.
    /// </summary>
    public static class QrCodeGenerator
    {
        /// <summary>
        /// Encodes the given text into a square QR Code module matrix.
        /// Uses byte-mode encoding, error-correction level MEDIUM, and automatic version selection.
        /// </summary>
        /// <param name="text">The text (e.g. a URL) to encode.</param>
        /// <returns>A square matrix where <c>true</c> = dark module. Indexed as [row, column].</returns>
        public static bool[,] Encode(string text)
        {
            QrCode qr = QrCode.EncodeText(text);
            int size = qr.Size;
            bool[,] result = new bool[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    result[y, x] = qr.GetModule(x, y);
                }
            }
            return result;
        }

        /// <summary>
        /// Encodes the given text and renders it to a Texture2D. Dark modules are black,
        /// light modules are white, with a white quiet-zone border of <paramref name="quietZoneModules"/>
        /// modules around the symbol. Each module is <paramref name="pixelsPerModule"/> pixels square.
        /// </summary>
        public static Texture2D GenerateTexture(string text, int pixelsPerModule = 8, int quietZoneModules = 4)
        {
            if (pixelsPerModule < 1)
            {
                pixelsPerModule = 1;
            }
            if (quietZoneModules < 0)
            {
                quietZoneModules = 0;
            }

            bool[,] matrix = Encode(text);
            int size = matrix.GetLength(0);
            int fullModules = size + quietZoneModules * 2;
            int dim = fullModules * pixelsPerModule;

            Texture2D tex = new Texture2D(dim, dim, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;

            Color32 white = new Color32(255, 255, 255, 255);
            Color32 black = new Color32(0, 0, 0, 255);

            Color32[] pixels = new Color32[dim * dim];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = white;
            }

            for (int r = 0; r < size; r++)
            {
                for (int c = 0; c < size; c++)
                {
                    if (!matrix[r, c])
                    {
                        continue;
                    }

                    // Module column/row to pixel coordinates. Texture origin is bottom-left,
                    // but QR row 0 is the top row, so flip vertically to render upright.
                    int pxLeft = (c + quietZoneModules) * pixelsPerModule;
                    int pixelsFromTopBase = (r + quietZoneModules) * pixelsPerModule;

                    for (int dy = 0; dy < pixelsPerModule; dy++)
                    {
                        int pixelFromTop = pixelsFromTopBase + dy;
                        int ty = dim - 1 - pixelFromTop;
                        int rowOffset = ty * dim;
                        for (int dx = 0; dx < pixelsPerModule; dx++)
                        {
                            int tx = pxLeft + dx;
                            pixels[rowOffset + tx] = black;
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Internal QR Code symbol builder. Fixed to error-correction level MEDIUM.
        /// </summary>
        private sealed class QrCode
        {
            // Error correction level MEDIUM: table index = 1, format-info bits = 0.
            private const int EccTableIndex = 1;
            private const int EccFormatBits = 0;

            private const int MinVersion = 1;
            private const int MaxVersion = 40;

            // Penalty constants used by the mask scoring algorithm.
            private const int PenaltyN1 = 3;
            private const int PenaltyN2 = 3;
            private const int PenaltyN3 = 40;
            private const int PenaltyN4 = 10;

            private readonly int version;
            private readonly int size;
            private readonly bool[,] modules;     // [y, x], true = dark
            private readonly bool[,] isFunction;  // [y, x], true = reserved/function module

            public int Size { get { return size; } }

            public bool GetModule(int x, int y)
            {
                return modules[y, x];
            }

            private QrCode(int ver, byte[] dataCodewords)
            {
                version = ver;
                size = ver * 4 + 17;
                modules = new bool[size, size];
                isFunction = new bool[size, size];

                DrawFunctionPatterns();
                byte[] allCodewords = AddEccAndInterleave(dataCodewords);
                DrawCodewords(allCodewords);

                // Pick the mask with the lowest penalty score.
                int minPenalty = int.MaxValue;
                int bestMask = 0;
                for (int m = 0; m < 8; m++)
                {
                    ApplyMask(m);
                    DrawFormatBits(m);
                    int penalty = GetPenaltyScore();
                    if (penalty < minPenalty)
                    {
                        minPenalty = penalty;
                        bestMask = m;
                    }
                    ApplyMask(m); // XOR again to undo
                }

                ApplyMask(bestMask);
                DrawFormatBits(bestMask);
            }

            // -------- High-level text encoding --------

            public static QrCode EncodeText(string text)
            {
                byte[] data = Encoding.UTF8.GetBytes(text ?? string.Empty);

                int version = -1;
                for (int v = MinVersion; v <= MaxVersion; v++)
                {
                    int ccBits = (v <= 9) ? 8 : 16; // byte-mode character count indicator
                    int usedBits = 4 + ccBits + data.Length * 8;
                    int capacityBits = GetNumDataCodewords(v) * 8;
                    if (usedBits <= capacityBits)
                    {
                        version = v;
                        break;
                    }
                }
                if (version == -1)
                {
                    throw new ArgumentException("Data too long to fit in any QR Code version at ECC level MEDIUM.");
                }

                int dataCapacityBits = GetNumDataCodewords(version) * 8;
                List<bool> bb = new List<bool>(dataCapacityBits);

                int ccBitsFinal = (version <= 9) ? 8 : 16;
                AppendBits(bb, 0x4, 4);                  // byte-mode indicator
                AppendBits(bb, data.Length, ccBitsFinal); // character count
                foreach (byte b in data)
                {
                    AppendBits(bb, b, 8);
                }

                // Terminator (up to 4 zero bits).
                int terminator = Math.Min(4, dataCapacityBits - bb.Count);
                AppendBits(bb, 0, terminator);

                // Pad to a byte boundary.
                AppendBits(bb, 0, (8 - bb.Count % 8) % 8);

                // Pad with alternating 0xEC, 0x11 until capacity is reached.
                for (int padByte = 0xEC; bb.Count < dataCapacityBits; padByte ^= 0xEC ^ 0x11)
                {
                    AppendBits(bb, padByte, 8);
                }

                byte[] dataCodewords = new byte[bb.Count / 8];
                for (int i = 0; i < bb.Count; i++)
                {
                    if (bb[i])
                    {
                        dataCodewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                    }
                }

                return new QrCode(version, dataCodewords);
            }

            private static void AppendBits(List<bool> bb, int val, int len)
            {
                for (int i = len - 1; i >= 0; i--)
                {
                    bb.Add(((val >> i) & 1) != 0);
                }
            }

            // -------- Function pattern drawing --------

            private void DrawFunctionPatterns()
            {
                // Timing patterns.
                for (int i = 0; i < size; i++)
                {
                    SetFunctionModule(6, i, i % 2 == 0);
                    SetFunctionModule(i, 6, i % 2 == 0);
                }

                // Three finder patterns (plus their separators).
                DrawFinderPattern(3, 3);
                DrawFinderPattern(size - 4, 3);
                DrawFinderPattern(3, size - 4);

                // Alignment patterns.
                int[] alignPos = GetAlignmentPatternPositions();
                int numAlign = alignPos.Length;
                for (int i = 0; i < numAlign; i++)
                {
                    for (int j = 0; j < numAlign; j++)
                    {
                        // Skip the three positions occupied by finder patterns.
                        if ((i == 0 && j == 0) || (i == 0 && j == numAlign - 1) || (i == numAlign - 1 && j == 0))
                        {
                            continue;
                        }
                        DrawAlignmentPattern(alignPos[i], alignPos[j]);
                    }
                }

                // Reserve format and version info areas (filled in later).
                DrawFormatBits(0);
                DrawVersion();
            }

            private void DrawFinderPattern(int x, int y)
            {
                for (int dy = -4; dy <= 4; dy++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        int xx = x + dx;
                        int yy = y + dy;
                        if (xx >= 0 && xx < size && yy >= 0 && yy < size)
                        {
                            SetFunctionModule(xx, yy, dist != 2 && dist != 4);
                        }
                    }
                }
            }

            private void DrawAlignmentPattern(int x, int y)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                    }
                }
            }

            private int[] GetAlignmentPatternPositions()
            {
                if (version == 1)
                {
                    return new int[0];
                }

                int numAlign = version / 7 + 2;
                int step;
                if (version == 32)
                {
                    step = 26;
                }
                else
                {
                    step = (version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
                }

                int[] result = new int[numAlign];
                result[0] = 6;
                for (int i = result.Length - 1, pos = size - 7; i >= 1; i--, pos -= step)
                {
                    result[i] = pos;
                }
                return result;
            }

            private void DrawFormatBits(int mask)
            {
                int data = EccFormatBits << 3 | mask;
                int rem = data;
                for (int i = 0; i < 10; i++)
                {
                    rem = (rem << 1) ^ ((rem >> 9) * 0x537);
                }
                int bits = (data << 10 | rem) ^ 0x5412;

                // First copy.
                for (int i = 0; i <= 5; i++)
                {
                    SetFunctionModule(8, i, GetBit(bits, i));
                }
                SetFunctionModule(8, 7, GetBit(bits, 6));
                SetFunctionModule(8, 8, GetBit(bits, 7));
                SetFunctionModule(7, 8, GetBit(bits, 8));
                for (int i = 9; i < 15; i++)
                {
                    SetFunctionModule(14 - i, 8, GetBit(bits, i));
                }

                // Second copy.
                for (int i = 0; i < 8; i++)
                {
                    SetFunctionModule(size - 1 - i, 8, GetBit(bits, i));
                }
                for (int i = 8; i < 15; i++)
                {
                    SetFunctionModule(8, size - 15 + i, GetBit(bits, i));
                }
                SetFunctionModule(8, size - 8, true); // Always dark.
            }

            private void DrawVersion()
            {
                if (version < 7)
                {
                    return;
                }

                int rem = version;
                for (int i = 0; i < 12; i++)
                {
                    rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
                }
                int bits = version << 12 | rem;

                for (int i = 0; i < 18; i++)
                {
                    bool bit = GetBit(bits, i);
                    int a = size - 11 + i % 3;
                    int b = i / 3;
                    SetFunctionModule(a, b, bit);
                    SetFunctionModule(b, a, bit);
                }
            }

            private void SetFunctionModule(int x, int y, bool isDark)
            {
                modules[y, x] = isDark;
                isFunction[y, x] = true;
            }

            // -------- Error correction & interleaving --------

            private byte[] AddEccAndInterleave(byte[] data)
            {
                int numBlocks = NumErrorCorrectionBlocks[EccTableIndex][version];
                int blockEccLen = EccCodewordsPerBlock[EccTableIndex][version];
                int rawCodewords = GetNumRawDataModules(version) / 8;
                int numShortBlocks = numBlocks - rawCodewords % numBlocks;
                int shortBlockLen = rawCodewords / numBlocks;

                byte[][] blocks = new byte[numBlocks][];
                byte[] rsDiv = ReedSolomonComputeDivisor(blockEccLen);
                for (int i = 0, k = 0; i < numBlocks; i++)
                {
                    int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
                    byte[] dat = new byte[datLen];
                    Array.Copy(data, k, dat, 0, datLen);
                    k += datLen;

                    byte[] block = new byte[shortBlockLen + 1];
                    Array.Copy(dat, 0, block, 0, dat.Length);
                    byte[] ecc = ReedSolomonComputeRemainder(dat, rsDiv);
                    Array.Copy(ecc, 0, block, block.Length - blockEccLen, ecc.Length);
                    blocks[i] = block;
                }

                byte[] result = new byte[rawCodewords];
                for (int i = 0, k = 0; i < blocks[0].Length; i++)
                {
                    for (int j = 0; j < blocks.Length; j++)
                    {
                        // Skip the unused padding cell in short blocks.
                        if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                        {
                            result[k] = blocks[j][i];
                            k++;
                        }
                    }
                }
                return result;
            }

            private void DrawCodewords(byte[] data)
            {
                int i = 0; // bit index into data
                for (int right = size - 1; right >= 1; right -= 2)
                {
                    if (right == 6)
                    {
                        right = 5;
                    }
                    for (int vert = 0; vert < size; vert++)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? size - 1 - vert : vert;
                            if (!isFunction[y, x] && i < data.Length * 8)
                            {
                                modules[y, x] = GetBit(data[i >> 3], 7 - (i & 7));
                                i++;
                            }
                        }
                    }
                }
            }

            private void ApplyMask(int mask)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool invert;
                        switch (mask)
                        {
                            case 0: invert = (x + y) % 2 == 0; break;
                            case 1: invert = y % 2 == 0; break;
                            case 2: invert = x % 3 == 0; break;
                            case 3: invert = (x + y) % 3 == 0; break;
                            case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                            case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            case 7: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                            default: throw new ArgumentException("Mask value out of range");
                        }
                        if (!isFunction[y, x] && invert)
                        {
                            modules[y, x] = !modules[y, x];
                        }
                    }
                }
            }

            // -------- Penalty scoring --------

            private int GetPenaltyScore()
            {
                int result = 0;

                // Adjacent modules in rows having the same color, plus finder-like patterns.
                for (int y = 0; y < size; y++)
                {
                    bool runColor = false;
                    int runLen = 0;
                    int[] runHistory = new int[7];
                    for (int x = 0; x < size; x++)
                    {
                        if (modules[y, x] == runColor)
                        {
                            runLen++;
                            if (runLen == 5)
                            {
                                result += PenaltyN1;
                            }
                            else if (runLen > 5)
                            {
                                result++;
                            }
                        }
                        else
                        {
                            FinderPenaltyAddHistory(runLen, runHistory);
                            if (!runColor)
                            {
                                result += FinderPenaltyCountPatterns(runHistory) * PenaltyN3;
                            }
                            runColor = modules[y, x];
                            runLen = 1;
                        }
                    }
                    result += FinderPenaltyTerminateAndCount(runColor, runLen, runHistory) * PenaltyN3;
                }

                // Adjacent modules in columns.
                for (int x = 0; x < size; x++)
                {
                    bool runColor = false;
                    int runLen = 0;
                    int[] runHistory = new int[7];
                    for (int y = 0; y < size; y++)
                    {
                        if (modules[y, x] == runColor)
                        {
                            runLen++;
                            if (runLen == 5)
                            {
                                result += PenaltyN1;
                            }
                            else if (runLen > 5)
                            {
                                result++;
                            }
                        }
                        else
                        {
                            FinderPenaltyAddHistory(runLen, runHistory);
                            if (!runColor)
                            {
                                result += FinderPenaltyCountPatterns(runHistory) * PenaltyN3;
                            }
                            runColor = modules[y, x];
                            runLen = 1;
                        }
                    }
                    result += FinderPenaltyTerminateAndCount(runColor, runLen, runHistory) * PenaltyN3;
                }

                // 2x2 blocks of the same color.
                for (int y = 0; y < size - 1; y++)
                {
                    for (int x = 0; x < size - 1; x++)
                    {
                        bool color = modules[y, x];
                        if (color == modules[y, x + 1] && color == modules[y + 1, x] && color == modules[y + 1, x + 1])
                        {
                            result += PenaltyN2;
                        }
                    }
                }

                // Balance of dark and light modules.
                int dark = 0;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (modules[y, x])
                        {
                            dark++;
                        }
                    }
                }
                int total = size * size;
                int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
                result += k * PenaltyN4;

                return result;
            }

            private int FinderPenaltyCountPatterns(int[] runHistory)
            {
                int n = runHistory[1];
                bool core = n > 0 && runHistory[2] == n && runHistory[3] == n * 3 && runHistory[4] == n && runHistory[5] == n;
                return (core && runHistory[0] >= n * 4 && runHistory[6] >= n ? 1 : 0)
                     + (core && runHistory[6] >= n * 4 && runHistory[0] >= n ? 1 : 0);
            }

            private int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength, int[] runHistory)
            {
                if (currentRunColor)
                {
                    FinderPenaltyAddHistory(currentRunLength, runHistory);
                    currentRunLength = 0;
                }
                currentRunLength += size; // add light border to final run
                FinderPenaltyAddHistory(currentRunLength, runHistory);
                return FinderPenaltyCountPatterns(runHistory);
            }

            private void FinderPenaltyAddHistory(int currentRunLength, int[] runHistory)
            {
                if (runHistory[0] == 0)
                {
                    currentRunLength += size; // add light border to initial run
                }
                Array.Copy(runHistory, 0, runHistory, 1, runHistory.Length - 1);
                runHistory[0] = currentRunLength;
            }

            // -------- Reed-Solomon (GF(256)) --------

            private static byte[] ReedSolomonComputeDivisor(int degree)
            {
                byte[] result = new byte[degree];
                result[degree - 1] = 1;
                int root = 1;
                for (int i = 0; i < degree; i++)
                {
                    for (int j = 0; j < result.Length; j++)
                    {
                        result[j] = (byte)ReedSolomonMultiply(result[j] & 0xFF, root);
                        if (j + 1 < result.Length)
                        {
                            result[j] ^= result[j + 1];
                        }
                    }
                    root = ReedSolomonMultiply(root, 0x02);
                }
                return result;
            }

            private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
            {
                byte[] result = new byte[divisor.Length];
                foreach (byte b in data)
                {
                    int factor = (b ^ result[0]) & 0xFF;
                    Array.Copy(result, 1, result, 0, result.Length - 1);
                    result[result.Length - 1] = 0;
                    for (int i = 0; i < result.Length; i++)
                    {
                        result[i] ^= (byte)ReedSolomonMultiply(divisor[i] & 0xFF, factor);
                    }
                }
                return result;
            }

            private static int ReedSolomonMultiply(int x, int y)
            {
                int z = 0;
                for (int i = 7; i >= 0; i--)
                {
                    z = (z << 1) ^ ((z >> 7) * 0x11D);
                    z ^= ((y >> i) & 1) * x;
                }
                return z & 0xFF;
            }

            // -------- Capacity helpers --------

            private static int GetNumRawDataModules(int ver)
            {
                int result = (16 * ver + 128) * ver + 64;
                if (ver >= 2)
                {
                    int numAlign = ver / 7 + 2;
                    result -= (25 * numAlign - 10) * numAlign - 55;
                    if (ver >= 7)
                    {
                        result -= 36;
                    }
                }
                return result;
            }

            private static int GetNumDataCodewords(int ver)
            {
                return GetNumRawDataModules(ver) / 8
                     - EccCodewordsPerBlock[EccTableIndex][ver] * NumErrorCorrectionBlocks[EccTableIndex][ver];
            }

            private static bool GetBit(int x, int i)
            {
                return ((x >> i) & 1) != 0;
            }

            // -------- Static tables (index 0 is padding; rows are ECC levels L, M, Q, H) --------

            private static readonly int[][] EccCodewordsPerBlock =
            {
                //  0   1   2   3   4   5   6   7   8   9  10  11  12  13  14  15  16  17  18  19  20  21  22  23  24  25  26  27  28  29  30  31  32  33  34  35  36  37  38  39  40
                new int[] {-1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30}, // L
                new int[] {-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28}, // M
                new int[] {-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30}, // Q
                new int[] {-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30}, // H
            };

            private static readonly int[][] NumErrorCorrectionBlocks =
            {
                //  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40
                new int[] {-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25}, // L
                new int[] {-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49}, // M
                new int[] {-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68}, // Q
                new int[] {-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81}, // H
            };
        }
    }
}
