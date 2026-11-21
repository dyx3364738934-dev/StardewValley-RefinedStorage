using System;
using System.Collections.Generic;
using System.Text;

namespace RefinedStorage.Ime
{
    public readonly struct PinyinCandidate
    {
        public string Text { get; }
        public int ConsumedInputLength { get; }
        public int Score { get; }

        public PinyinCandidate(string text, int consumedInputLength, int score)
        {
            // 独立逻辑，无上文
            // 作用：构造不可变拼音候选词条目，记录候选文本、消耗拼音长度与排序分值
            Text = text;
            ConsumedInputLength = consumedInputLength;
            Score = score;
        }
    }

    public sealed class PinyinQueryResult
    {
        public string RawInput { get; }
        public string SegmentedDisplay { get; }
        public IReadOnlyList<PinyinCandidate> Candidates { get; }

        public PinyinQueryResult(string rawInput, string segmentedDisplay, IReadOnlyList<PinyinCandidate> candidates)
        {
            // 独立逻辑，无上文
            // 作用：封装一次拼音查询的原始输入、切分展示串与有序候选列表
            RawInput = rawInput;
            SegmentedDisplay = segmentedDisplay;
            Candidates = candidates;
        }
    }

    public static class PinyinEngine
    {
        private readonly struct SegmentDpState
        {
            public readonly int FullChars;
            public readonly int PrefixChars;
            public readonly int InvalidChars;
            public readonly int SegmentCount;
            public readonly int LengthSquareSum;
            public readonly int PrevIndex;
            public readonly bool Reachable;

            public SegmentDpState(int fullChars, int prefixChars, int invalidChars, int segmentCount, int lengthSquareSum, int prevIndex, bool reachable)
            {
                // 独立逻辑，无上文
                // 作用：存储拼音动态规划切分到达当前下标时的多维最优状态
                FullChars = fullChars;
                PrefixChars = prefixChars;
                InvalidChars = invalidChars;
                SegmentCount = segmentCount;
                LengthSquareSum = lengthSquareSum;
                PrevIndex = prevIndex;
                Reachable = reachable;
            }

            public bool IsBetterThan(in SegmentDpState other)
            {
                // 与上文：在动态规划转移时对比新旧切分方案
                // 作用：按完整音节字数优先、残缺极少、切分段数最少与长音节偏好进行字典序优劣裁决
                if (!other.Reachable)
                {
                    return true;
                }
                if (FullChars != other.FullChars)
                {
                    return FullChars > other.FullChars;
                }
                if (InvalidChars != other.InvalidChars)
                {
                    return InvalidChars < other.InvalidChars;
                }
                if (PrefixChars != other.PrefixChars)
                {
                    return PrefixChars > other.PrefixChars;
                }
                if (SegmentCount != other.SegmentCount)
                {
                    return SegmentCount < other.SegmentCount;
                }
                return LengthSquareSum > other.LengthSquareSum;
            }
        }

        private readonly struct PhraseMatchInfo
        {
            public readonly bool Matched;
            public readonly bool IsExactFull;
            public readonly bool IsCompleteInitials;
            public readonly bool AllSyllablesMatched;
            public readonly int MatchedSyllables;

            public PhraseMatchInfo(bool matched, bool isExactFull, bool isCompleteInitials, bool allSyllablesMatched, int matchedSyllables)
            {
                // 独立逻辑，无上文
                // 作用：描述词条对当前拼音串的命中类型与覆盖音节数量
                Matched = matched;
                IsExactFull = isExactFull;
                IsCompleteInitials = isCompleteInitials;
                AllSyllablesMatched = allSyllablesMatched;
                MatchedSyllables = matchedSyllables;
            }
        }

        public static IReadOnlyList<string> Segment(string rawInput)
        {
            // 与上文：依赖 PinyinLexicon.Instance 提供的规范音节表与合法前缀集合
            // 作用：清洗输入为纯小写字母并执行动态规划切分，返回最优音节切片序列
            string clean = CleanAsciiLetters(rawInput);
            if (clean.Length == 0)
            {
                return Array.Empty<string>();
            }

            return SegmentClean(clean, PinyinLexicon.Instance);
        }

        public static PinyinQueryResult Query(string rawInput, ISet<string>? priorityWords = null, int maxCandidates = 60)
        {
            // 与上文：串联 SegmentClean 切分、PinyinLexicon 词库检索与单字倒排表
            // 作用：综合全拼匹配、前缀分步选词、单字候选与箱内优先词加权，返回去重降序后的查询结果
            string raw = rawInput ?? string.Empty;
            string clean = CleanAsciiLetters(raw);
            if (clean.Length == 0 || maxCandidates <= 0)
            {
                return new PinyinQueryResult(raw, string.Empty, Array.Empty<PinyinCandidate>());
            }

            PinyinLexicon lexicon = PinyinLexicon.Instance;
            IReadOnlyList<string> segments = SegmentClean(clean, lexicon);
            string segmentedDisplay = string.Join("'", segments);

            Dictionary<string, PinyinCandidate> bestByText = new Dictionary<string, PinyinCandidate>(StringComparer.Ordinal);
            List<PinyinLexicon.PhraseEntry> candidatePhrases = CollectCandidatePhrases(lexicon, clean[0], priorityWords);

            CollectPhraseCandidates(candidatePhrases, clean, priorityWords, bestByText);
            CollectSingleCharCandidates(lexicon, clean, segments, priorityWords, maxCandidates, bestByText);

            List<PinyinCandidate> sorted = new List<PinyinCandidate>(bestByText.Values);
            sorted.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0)
                {
                    return cmp;
                }
                cmp = b.ConsumedInputLength.CompareTo(a.ConsumedInputLength);
                if (cmp != 0)
                {
                    return cmp;
                }
                return string.CompareOrdinal(a.Text, b.Text);
            });

            if (sorted.Count > maxCandidates)
            {
                sorted.RemoveRange(maxCandidates, sorted.Count - maxCandidates);
            }

            return new PinyinQueryResult(raw, segmentedDisplay, sorted);
        }

        private static List<string> SegmentClean(string clean, PinyinLexicon lexicon)
        {
            // 与上文：在 Segment 与 Query 入口完成输入清洗后调用
            // 作用：基于 1..6 字母滑窗枚举完整音节与合法前缀进行最优动态规划切分并回溯还原分段
            int n = clean.Length;
            SegmentDpState[] dp = new SegmentDpState[n + 1];
            dp[0] = new SegmentDpState(0, 0, 0, 0, 0, -1, true);

            for (int i = 0; i < n; i++)
            {
                if (!dp[i].Reachable)
                {
                    continue;
                }

                int maxEnd = Math.Min(n, i + 6);
                for (int j = i + 1; j <= maxEnd; j++)
                {
                    string sub = clean.Substring(i, j - i);
                    int len = j - i;

                    if (lexicon.IsValidSyllable(sub))
                    {
                        SegmentDpState cand = new SegmentDpState(
                            dp[i].FullChars + len,
                            dp[i].PrefixChars,
                            dp[i].InvalidChars,
                            dp[i].SegmentCount + 1,
                            dp[i].LengthSquareSum + len * len,
                            i,
                            true);
                        if (cand.IsBetterThan(dp[j]))
                        {
                            dp[j] = cand;
                        }
                    }
                    else if (lexicon.HasSyllablePrefix(sub))
                    {
                        SegmentDpState cand = new SegmentDpState(
                            dp[i].FullChars,
                            dp[i].PrefixChars + len,
                            dp[i].InvalidChars,
                            dp[i].SegmentCount + 1,
                            dp[i].LengthSquareSum + len * len,
                            i,
                            true);
                        if (cand.IsBetterThan(dp[j]))
                        {
                            dp[j] = cand;
                        }
                    }
                    else if (len == 1)
                    {
                        SegmentDpState cand = new SegmentDpState(
                            dp[i].FullChars,
                            dp[i].PrefixChars,
                            dp[i].InvalidChars + 1,
                            dp[i].SegmentCount + 1,
                            dp[i].LengthSquareSum + 1,
                            i,
                            true);
                        if (cand.IsBetterThan(dp[j]))
                        {
                            dp[j] = cand;
                        }
                    }
                }
            }

            List<string> result = new List<string>();
            int cur = n;
            while (cur > 0)
            {
                int prev = dp[cur].PrevIndex;
                if (prev < 0)
                {
                    break;
                }
                result.Add(clean.Substring(prev, cur - prev));
                cur = prev;
            }
            result.Reverse();
            return result;
        }

        private static List<PinyinLexicon.PhraseEntry> CollectCandidatePhrases(
            PinyinLexicon lexicon,
            char firstChar,
            ISet<string>? priorityWords)
        {
            // 与上文：依据输入首字母从 PinyinLexicon 提取词条桶并融合当前箱内动态词
            // 作用：确保既覆盖已注册星露谷词条，也兼容箱内未提前入库的自定义中文物品名
            List<PinyinLexicon.PhraseEntry> phrases = lexicon.GetPhraseBucketSnapshot(firstChar);
            if (priorityWords == null || priorityWords.Count == 0)
            {
                return phrases;
            }

            foreach (string word in priorityWords)
            {
                if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
                {
                    continue;
                }

                string trimmed = word.Trim();
                if (lexicon.TryGetRegisteredPhrase(trimmed, out _))
                {
                    continue;
                }

                PinyinLexicon.PhraseEntry? dynamicEntry = lexicon.TryBuildPhraseEntry(trimmed, 240);
                if (dynamicEntry == null)
                {
                    continue;
                }

                for (int i = 0; i < dynamicEntry.InitialsList.Length; i++)
                {
                    if (dynamicEntry.InitialsList[i].Length > 0 && dynamicEntry.InitialsList[i][0] == firstChar)
                    {
                        phrases.Add(dynamicEntry);
                        break;
                    }
                }
            }

            return phrases;
        }

        private static void CollectPhraseCandidates(
            List<PinyinLexicon.PhraseEntry> phrases,
            string clean,
            ISet<string>? priorityWords,
            Dictionary<string, PinyinCandidate> bestByText)
        {
            // 与上文：接收 CollectCandidatePhrases 收集的候选词条列表
            // 作用：先后匹配完整输入与分步前缀输入并计算多字词候选评分（箱内词叠加 +1_000_000）
            for (int p = 0; p < phrases.Count; p++)
            {
                PinyinLexicon.PhraseEntry phrase = phrases[p];
                bool isPriority = priorityWords != null && priorityWords.Contains(phrase.Word);

                PhraseMatchInfo fullMatch = EvaluatePhraseMatch(phrase, clean);
                if (fullMatch.Matched)
                {
                    int score;
                    if (fullMatch.IsExactFull)
                    {
                        score = 650_000 + clean.Length * 15_000 + phrase.Weight * 10;
                    }
                    else if (fullMatch.IsCompleteInitials)
                    {
                        score = 460_000 + clean.Length * 15_000 + phrase.Weight * 10;
                    }
                    else if (fullMatch.AllSyllablesMatched)
                    {
                        score = 440_000 + clean.Length * 15_000 + phrase.Weight * 10;
                    }
                    else if (fullMatch.MatchedSyllables >= 2)
                    {
                        score = 350_000 + fullMatch.MatchedSyllables * 10_000 + clean.Length * 12_000 + phrase.Weight * 10;
                    }
                    else if (clean.Length == 1)
                    {
                        score = 80_000 + phrase.Weight * 5;
                    }
                    else
                    {
                        score = 140_000 + clean.Length * 8_000 + phrase.Weight * 10;
                    }

                    if (isPriority)
                    {
                        score += 1_000_000;
                    }

                    AddCandidate(bestByText, phrase.Word, clean.Length, score);
                }

                if (clean.Length <= 2)
                {
                    continue;
                }

                for (int prefixLen = clean.Length - 1; prefixLen >= 2; prefixLen--)
                {
                    string prefix = clean.Substring(0, prefixLen);
                    PhraseMatchInfo prefixMatch = EvaluatePhraseMatch(phrase, prefix);
                    if (!prefixMatch.Matched || !prefixMatch.AllSyllablesMatched)
                    {
                        continue;
                    }

                    if (!prefixMatch.IsExactFull && (prefixMatch.IsCompleteInitials || prefixLen < 4))
                    {
                        continue;
                    }

                    int score = (prefixMatch.IsExactFull ? 260_000 : 200_000)
                        + prefixLen * 25_000
                        + phrase.Weight * 10;
                    if (isPriority)
                    {
                        score += 1_000_000;
                    }

                    AddCandidate(bestByText, phrase.Word, prefixLen, score);
                    break;
                }
            }
        }

        private static void CollectSingleCharCandidates(
            PinyinLexicon lexicon,
            string clean,
            IReadOnlyList<string> segments,
            ISet<string>? priorityWords,
            int maxCandidates,
            Dictionary<string, PinyinCandidate> bestByText)
        {
            // 与上文：在多字词匹配后补充首音节及起始合法音节对应的单字候选
            // 作用：按完整音节优先于前缀音节与字频降序为单字赋分，并支持首字分步上屏
            string firstSeg = segments[0];
            int charLimit = Math.Max(maxCandidates, 60);

            if (lexicon.IsValidSyllable(firstSeg))
            {
                IReadOnlyList<char> exactChars = lexicon.GetCharsForSyllable(firstSeg);
                int count = Math.Min(exactChars.Count, charLimit);
                for (int i = 0; i < count; i++)
                {
                    string text = exactChars[i].ToString();
                    int score = 260_000 + firstSeg.Length * 10_000 - i * 15;
                    if (priorityWords != null && priorityWords.Contains(text))
                    {
                        score += 1_000_000;
                    }
                    AddCandidate(bestByText, text, firstSeg.Length, score);
                }
            }
            else if (lexicon.HasSyllablePrefix(firstSeg))
            {
                IReadOnlyList<char> prefixChars = lexicon.GetCharsByPrefix(firstSeg, charLimit);
                for (int i = 0; i < prefixChars.Count; i++)
                {
                    string text = prefixChars[i].ToString();
                    int score = 160_000 + firstSeg.Length * 5_000 - i * 15;
                    if (priorityWords != null && priorityWords.Contains(text))
                    {
                        score += 1_000_000;
                    }
                    AddCandidate(bestByText, text, firstSeg.Length, score);
                }
            }

            int maxAltLen = Math.Min(clean.Length, 6);
            for (int len = maxAltLen; len >= 1; len--)
            {
                if (len == firstSeg.Length)
                {
                    continue;
                }

                string sub = clean.Substring(0, len);
                if (!lexicon.IsValidSyllable(sub))
                {
                    continue;
                }

                IReadOnlyList<char> altChars = lexicon.GetCharsForSyllable(sub);
                int take = Math.Min(altChars.Count, 20);
                for (int i = 0; i < take; i++)
                {
                    string text = altChars[i].ToString();
                    int score = 180_000 + len * 5_000 - i * 15;
                    if (priorityWords != null && priorityWords.Contains(text))
                    {
                        score += 1_000_000;
                    }
                    AddCandidate(bestByText, text, len, score);
                }
            }
        }

        private static PhraseMatchInfo EvaluatePhraseMatch(PinyinLexicon.PhraseEntry phrase, string query)
        {
            // 与上文：针对词条的各多音字路径评估 query 匹配度
            // 作用：选取最优的完整全拼、完整简拼、全音节覆盖或部分前缀命中结果
            PhraseMatchInfo best = default;
            for (int i = 0; i < phrase.SyllableLists.Length; i++)
            {
                string[] syls = phrase.SyllableLists[i];
                bool isExactFull = string.Equals(phrase.FullPinyins[i], query, StringComparison.Ordinal);
                bool isCompleteInitials = string.Equals(phrase.InitialsList[i], query, StringComparison.Ordinal);

                if (isExactFull)
                {
                    return new PhraseMatchInfo(true, true, isCompleteInitials, true, syls.Length);
                }

                if (isCompleteInitials)
                {
                    PhraseMatchInfo cand = new PhraseMatchInfo(true, false, true, true, syls.Length);
                    if (!best.Matched || (!best.IsCompleteInitials && cand.IsCompleteInitials))
                    {
                        best = cand;
                    }
                    continue;
                }

                int maxMatched = MatchSyllableSequence(syls, 0, query, 0);
                if (maxMatched > 0)
                {
                    bool allMatched = maxMatched >= syls.Length;
                    PhraseMatchInfo cand = new PhraseMatchInfo(true, false, false, allMatched, maxMatched);
                    if (!best.Matched
                        || (cand.AllSyllablesMatched && !best.AllSyllablesMatched)
                        || (cand.AllSyllablesMatched == best.AllSyllablesMatched && cand.MatchedSyllables > best.MatchedSyllables))
                    {
                        best = cand;
                    }
                }
            }

            return best;
        }

        private static int MatchSyllableSequence(string[] syls, int sylIdx, string query, int pos)
        {
            // 与上文：由 EvaluatePhraseMatch 递归校验词条音节串对 query 的消费能力
            // 作用：支持每个汉字以完整音节、首字母简拼或查询末尾前缀进行消费，返回消费完毕时覆盖的音节数
            if (pos >= query.Length)
            {
                return sylIdx;
            }

            if (sylIdx >= syls.Length)
            {
                return 0;
            }

            string syl = syls[sylIdx];
            ReadOnlySpan<char> rem = query.AsSpan(pos);
            int best = 0;

            if (rem.Length >= syl.Length)
            {
                if (rem.StartsWith(syl.AsSpan(), StringComparison.Ordinal))
                {
                    int res = MatchSyllableSequence(syls, sylIdx + 1, query, pos + syl.Length);
                    if (res > best)
                    {
                        best = res;
                    }
                }
            }
            else if (syl.AsSpan().StartsWith(rem, StringComparison.Ordinal))
            {
                int res = sylIdx + 1;
                if (res > best)
                {
                    best = res;
                }
            }

            if (rem[0] == syl[0])
            {
                int res = MatchSyllableSequence(syls, sylIdx + 1, query, pos + 1);
                if (res > best)
                {
                    best = res;
                }
            }

            return best;
        }

        private static void AddCandidate(Dictionary<string, PinyinCandidate> bestByText, string text, int consumed, int score)
        {
            // 独立逻辑，无上文
            // 作用：对同名候选词去重，保留最高评分及在同分下更长的输入消费长度
            if (!bestByText.TryGetValue(text, out PinyinCandidate existing)
                || score > existing.Score
                || (score == existing.Score && consumed > existing.ConsumedInputLength))
            {
                bestByText[text] = new PinyinCandidate(text, consumed, score);
            }
        }

        private static string CleanAsciiLetters(string? rawInput)
        {
            // 独立逻辑，无上文
            // 作用：清洗原始拼音输入，仅保留小写 a-z 英文字母用于音节切分与词库检索
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(rawInput.Length);
            for (int i = 0; i < rawInput.Length; i++)
            {
                char c = char.ToLowerInvariant(rawInput[i]);
                if (c >= 'a' && c <= 'z')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
