using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace RefinedStorage.Ime
{
    public sealed class PinyinLexicon
    {
        internal sealed class PhraseEntry
        {
            public string Word { get; }
            public int Weight { get; set; }
            public string[] FullPinyins { get; }
            public string[][] SyllableLists { get; }
            public string[] InitialsList { get; }

            public PhraseEntry(string word, int weight, string[] fullPinyins, string[][] syllableLists, string[] initialsList)
            {
                // 独立逻辑，无上文
                // 作用：封装词条文本、权重及其展开后的多音字全拼与首字母简拼索引
                Word = word;
                Weight = weight;
                FullPinyins = fullPinyins;
                SyllableLists = syllableLists;
                InitialsList = initialsList;
            }
        }

        private const string ResourceName = "RefinedStorage.Assets.pinyin_chars.txt";
        private static readonly Lazy<PinyinLexicon> LazyInstance = new Lazy<PinyinLexicon>(() => new PinyinLexicon());

        private static readonly string[] StandardMandarinSyllables =
        {
            "a", "ai", "an", "ang", "ao",
            "ba", "bai", "ban", "bang", "bao", "bei", "ben", "beng", "bi", "bian", "biao", "bie", "bin", "bing", "bo", "bu",
            "ca", "cai", "can", "cang", "cao", "ce", "cen", "ceng", "cha", "chai", "chan", "chang", "chao", "che", "chen", "cheng", "chi", "chong", "chou", "chu", "chua", "chuai", "chuan", "chuang", "chui", "chun", "chuo", "ci", "cong", "cou", "cu", "cuan", "cui", "cun", "cuo",
            "da", "dai", "dan", "dang", "dao", "de", "dei", "den", "deng", "di", "dia", "dian", "diao", "die", "ding", "diu", "dong", "dou", "du", "duan", "dui", "dun", "duo",
            "e", "ei", "en", "eng", "er",
            "fa", "fan", "fang", "fei", "fen", "feng", "fo", "fou", "fu",
            "ga", "gai", "gan", "gang", "gao", "ge", "gei", "gen", "geng", "gong", "gou", "gu", "gua", "guai", "guan", "guang", "gui", "gun", "guo",
            "ha", "hai", "han", "hang", "hao", "he", "hei", "hen", "heng", "hong", "hou", "hu", "hua", "huai", "huan", "huang", "hui", "hun", "huo",
            "ji", "jia", "jian", "jiang", "jiao", "jie", "jin", "jing", "jiong", "jiu", "ju", "juan", "jue", "jun",
            "ka", "kai", "kan", "kang", "kao", "ke", "kei", "ken", "keng", "kong", "kou", "ku", "kua", "kuai", "kuan", "kuang", "kui", "kun", "kuo",
            "la", "lai", "lan", "lang", "lao", "le", "lei", "leng", "li", "lia", "lian", "liang", "liao", "lie", "lin", "ling", "liu", "lo", "long", "lou", "lu", "luan", "lue", "lun", "luo", "lv", "lve",
            "ma", "mai", "man", "mang", "mao", "me", "mei", "men", "meng", "mi", "mian", "miao", "mie", "min", "ming", "miu", "mo", "mou", "mu",
            "na", "nai", "nan", "nang", "nao", "ne", "nei", "nen", "neng", "ni", "nian", "niang", "niao", "nie", "nin", "ning", "niu", "nong", "nou", "nu", "nuan", "nue", "nuo", "nv", "nve",
            "o", "ou",
            "pa", "pai", "pan", "pang", "pao", "pei", "pen", "peng", "pi", "pian", "piao", "pie", "pin", "ping", "po", "pou", "pu",
            "qi", "qia", "qian", "qiang", "qiao", "qie", "qin", "qing", "qiong", "qiu", "qu", "quan", "que", "qun",
            "ran", "rang", "rao", "re", "ren", "reng", "ri", "rong", "rou", "ru", "ruan", "rui", "run", "ruo",
            "sa", "sai", "san", "sang", "sao", "se", "sen", "seng", "sha", "shai", "shan", "shang", "shao", "she", "shei", "shen", "sheng", "shi", "shou", "shu", "shua", "shuai", "shuan", "shuang", "shui", "shun", "shuo", "si", "song", "sou", "su", "suan", "sui", "sun", "suo",
            "ta", "tai", "tan", "tang", "tao", "te", "tei", "teng", "ti", "tian", "tiao", "tie", "ting", "tong", "tou", "tu", "tuan", "tui", "tun", "tuo",
            "wa", "wai", "wan", "wang", "wei", "wen", "weng", "wo", "wu",
            "xi", "xia", "xian", "xiang", "xiao", "xie", "xin", "xing", "xiong", "xiu", "xu", "xuan", "xue", "xun",
            "ya", "yan", "yang", "yao", "ye", "yi", "yin", "ying", "yo", "yong", "you", "yu", "yuan", "yue", "yun",
            "za", "zai", "zan", "zang", "zao", "ze", "zei", "zen", "zeng", "zha", "zhai", "zhan", "zhang", "zhao", "zhe", "zhei", "zhen", "zheng", "zhi", "zhong", "zhou", "zhu", "zhua", "zhuai", "zhuan", "zhuang", "zhui", "zhun", "zhuo", "zi", "zong", "zou", "zu", "zuan", "zui", "zun", "zuo"
        };

        private static readonly (string Word, int Weight)[] StardewSeedPhrases =
        {
            ("木头", 500), ("硬木", 480), ("石头", 500), ("铜矿石", 470), ("铁矿石", 470), ("金矿石", 470), ("铱矿石", 470),
            ("煤炭", 480), ("纤维", 460), ("树液", 450), ("粘土", 450), ("树枝", 440), ("五彩碎片", 490), ("上古水果", 490),
            ("防风草", 460), ("草莓", 460), ("蓝莓", 460), ("啤酒花", 450), ("杨桃", 470), ("宝石甜莓", 470),
            ("宝箱", 480), ("大箱子", 480), ("石箱", 460), ("大石箱", 460), ("祝尼魔箱", 430), ("迷你出货箱", 420), ("冰箱", 420),
            ("工作台", 470), ("熔炉", 470), ("小桶", 480), ("腌菜桶", 470), ("洒水器", 460), ("优质洒水器", 470), ("铱制洒水器", 480),
            ("电池组", 460), ("太阳精华", 450), ("虚空精华", 450),
            ("绿宝石", 460), ("钻石", 470), ("红宝石", 450), ("翡翠", 450), ("海蓝宝石", 450), ("紫水晶", 450), ("黄水晶", 450), ("石英", 450), ("精炼石英", 460),
            ("祝尼魔", 430), ("蛋黄酱", 450), ("奶酪", 450), ("松露油", 460), ("果酒", 470), ("果酱", 440), ("蜂蜜", 440), ("咖啡", 440),
            ("苹果", 420), ("橙子", 420), ("桃子", 420), ("石榴", 420), ("樱桃", 420), ("葡萄", 420),
            ("土豆", 430), ("花椰菜", 430), ("大蒜", 410), ("羽衣甘蓝", 420), ("大黄", 410), ("甜瓜", 430), ("辣椒", 420), ("萝卜", 410),
            ("红叶卷心菜", 420), ("小麦", 430), ("玉米", 430), ("茄子", 420), ("南瓜", 440), ("山药", 410), ("蔓越莓", 440),
            ("铜锭", 450), ("铁锭", 450), ("金锭", 450), ("铱锭", 460), ("枫糖浆", 430), ("橡树树脂", 430), ("松焦油", 430),
            ("远古种子", 440), ("混合种子", 410), ("鱼饵", 420), ("蟹笼", 410),
            ("金星", 400), ("银星", 400), ("铱星", 400),
            ("工具", 380), ("武器", 380), ("家具", 380), ("种子", 410), ("肥料", 410), ("矿石", 430), ("宝石", 420),
            ("水果", 400), ("蔬菜", 400), ("鱼", 420), ("菜肴", 390), ("手工艺品", 390), ("工匠物品", 390), ("矿物", 390), ("资源", 390), ("烹饪", 380)
        };

        private readonly Dictionary<char, string[]> _charToPinyins = new Dictionary<char, string[]>();
        private readonly Dictionary<char, int> _charOrderRank = new Dictionary<char, int>();
        private readonly Dictionary<string, List<char>> _syllableToChars = new Dictionary<string, List<char>>(StringComparer.Ordinal);
        private readonly HashSet<string> _validSyllables = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _syllablePrefixes = new HashSet<string>(StringComparer.Ordinal);

        private readonly object _phraseLock = new object();
        private readonly Dictionary<string, PhraseEntry> _phrasesByWord = new Dictionary<string, PhraseEntry>(StringComparer.Ordinal);
        private readonly Dictionary<char, List<PhraseEntry>> _phrasesByInitial = new Dictionary<char, List<PhraseEntry>>();

        private readonly object _vocabLock = new object();
        private bool _gameVocabularyLoaded;

        public static PinyinLexicon Instance => LazyInstance.Value;
        public int CharacterCount => _charToPinyins.Count;

        public int PhraseCount
        {
            get
            {
                // 独立逻辑，无上文
                // 作用：加锁读取当前词库中已注册词条总数
                lock (_phraseLock)
                {
                    return _phrasesByWord.Count;
                }
            }
        }

        private PinyinLexicon()
        {
            // 独立逻辑，无上文
            // 作用：初始化普通话规范音节集合，加载内嵌 8105 字表并注入高频星露谷词条种子
            RegisterStandardSyllables();
            LoadEmbeddedCharTable();
            RegisterSeedPhrases();
        }

        public IReadOnlyList<string> GetCharPinyins(char c)
        {
            // 独立逻辑，无上文
            // 作用：查询单个汉字对应的规范无调号拼音列表
            return _charToPinyins.TryGetValue(c, out string[]? pinyins) ? pinyins : Array.Empty<string>();
        }

        public IReadOnlyList<char> GetCharsForSyllable(string syllable)
        {
            // 独立逻辑，无上文
            // 作用：按高频优先序返回完全匹配指定拼音音节的汉字只读视图
            string normalized = NormalizeSyllableKey(syllable);
            if (normalized.Length == 0)
            {
                return Array.Empty<char>();
            }

            return _syllableToChars.TryGetValue(normalized, out List<char>? list) ? list : Array.Empty<char>();
        }

        public IReadOnlyList<char> GetCharsByPrefix(string syllablePrefix, int limit = 20)
        {
            // 与上文：复用 _syllableToChars 与字表高频序 _charOrderRank
            // 作用：返回以指定拼音前缀开头的常用单字列表，优先精确音节并按字频排序截断
            if (limit <= 0)
            {
                return Array.Empty<char>();
            }

            string normalized = NormalizeSyllableKey(syllablePrefix);
            if (normalized.Length == 0 || !_syllablePrefixes.Contains(normalized))
            {
                return Array.Empty<char>();
            }

            List<char> result = new List<char>(Math.Min(limit, 32));
            HashSet<char> seen = new HashSet<char>();

            if (_syllableToChars.TryGetValue(normalized, out List<char>? exactChars))
            {
                for (int i = 0; i < exactChars.Count && result.Count < limit; i++)
                {
                    char c = exactChars[i];
                    if (seen.Add(c))
                    {
                        result.Add(c);
                    }
                }
            }

            if (result.Count >= limit)
            {
                return result;
            }

            List<char> extended = new List<char>();
            foreach (KeyValuePair<string, List<char>> kvp in _syllableToChars)
            {
                if (kvp.Key.Length <= normalized.Length || !kvp.Key.StartsWith(normalized, StringComparison.Ordinal))
                {
                    continue;
                }

                List<char> chars = kvp.Value;
                int take = Math.Min(chars.Count, limit);
                for (int i = 0; i < take; i++)
                {
                    char c = chars[i];
                    if (seen.Add(c))
                    {
                        extended.Add(c);
                    }
                }
            }

            extended.Sort((a, b) => GetCharRank(a).CompareTo(GetCharRank(b)));
            for (int i = 0; i < extended.Count && result.Count < limit; i++)
            {
                result.Add(extended[i]);
            }

            return result;
        }

        public bool IsValidSyllable(string syllable)
        {
            // 独立逻辑，无上文
            // 作用：判定输入串是否为完整规范的普通话拼音音节
            string normalized = NormalizeSyllableKey(syllable);
            return normalized.Length > 0 && _validSyllables.Contains(normalized);
        }

        public bool HasSyllablePrefix(string prefix)
        {
            // 独立逻辑，无上文
            // 作用：判定输入串是否为至少一个规范拼音音节的合法前缀
            string normalized = NormalizeSyllableKey(prefix);
            return normalized.Length > 0 && _syllablePrefixes.Contains(normalized);
        }

        public void RegisterPhrase(string word, int baseWeight = 100)
        {
            // 与上文：依赖已加载的 _charToPinyins 字表展开多音字读音组合
            // 作用：将含汉字词条注册入全拼与首字母简拼检索索引，保留较高词频权重
            if (string.IsNullOrWhiteSpace(word))
            {
                return;
            }

            string trimmed = word.Trim();
            PhraseEntry? entry = TryBuildPhraseEntry(trimmed, baseWeight);
            if (entry == null)
            {
                return;
            }

            lock (_phraseLock)
            {
                if (_phrasesByWord.TryGetValue(trimmed, out PhraseEntry? existing))
                {
                    if (baseWeight > existing.Weight)
                    {
                        existing.Weight = baseWeight;
                    }
                    return;
                }

                _phrasesByWord[trimmed] = entry;
                HashSet<char> initialKeys = new HashSet<char>();
                for (int i = 0; i < entry.InitialsList.Length; i++)
                {
                    string initials = entry.InitialsList[i];
                    if (initials.Length == 0)
                    {
                        continue;
                    }

                    char first = initials[0];
                    if (!initialKeys.Add(first))
                    {
                        continue;
                    }

                    if (!_phrasesByInitial.TryGetValue(first, out List<PhraseEntry>? bucket))
                    {
                        bucket = new List<PhraseEntry>();
                        _phrasesByInitial[first] = bucket;
                    }
                    bucket.Add(entry);
                }
            }
        }

        public void EnsureGameVocabularyLoaded()
        {
            // 与上文：复用 RegisterPhrase 将运行时物品显示名写入词组检索表
            // 作用：线程安全且只执行一次地遍历游戏 ItemRegistry 物品定义并批量注入中文物品名
            lock (_vocabLock)
            {
                if (_gameVocabularyLoaded)
                {
                    return;
                }

                try
                {
                    IList<IItemDataDefinition>? itemTypes = ItemRegistry.ItemTypes;
                    if (itemTypes == null || itemTypes.Count == 0)
                    {
                        return;
                    }

                    int loadedCount = 0;
                    for (int i = 0; i < itemTypes.Count; i++)
                    {
                        IItemDataDefinition? definition = itemTypes[i];
                        if (definition == null)
                        {
                            continue;
                        }

                        try
                        {
                            IEnumerable<string>? ids = definition.GetAllIds();
                            if (ids == null)
                            {
                                continue;
                            }

                            foreach (string id in ids)
                            {
                                ParsedItemData? data = definition.GetData(id);
                                string? displayName = data?.DisplayName;
                                if (string.IsNullOrWhiteSpace(displayName))
                                {
                                    continue;
                                }

                                string trimmed = displayName.Trim();
                                if (trimmed.Length >= 2 && trimmed.Length <= 16 && ContainsKnownHanzi(trimmed))
                                {
                                    RegisterPhrase(trimmed, 220);
                                    loadedCount++;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }

                    if (loadedCount > 0)
                    {
                        _gameVocabularyLoaded = true;
                    }
                }
                catch
                {
                }
            }
        }

        public bool MatchesPinyinOrInitials(string? text, string? asciiQuery)
        {
            // 与上文：基于已加载字表读取待匹配文本各汉字的 v/u 归一化读音集合
            // 作用：判断文本中的汉字序列是否通过全拼、连续音节前缀或首字母简拼（含多音字与子串）命中查询串
            string cleanQuery = CleanQueryForMatching(asciiQuery);
            if (cleanQuery.Length == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            List<string[]> hanziReadings = ExtractHanziReadings(text);
            if (hanziReadings.Count == 0)
            {
                return false;
            }

            bool[] memoFailed = new bool[hanziReadings.Count * cleanQuery.Length];
            for (int startIdx = 0; startIdx < hanziReadings.Count; startIdx++)
            {
                if (hanziReadings[startIdx].Length == 0)
                {
                    continue;
                }

                if (CanMatchFrom(hanziReadings, startIdx, 0, cleanQuery, memoFailed))
                {
                    return true;
                }
            }

            return false;
        }

        internal int GetCharRank(char c)
        {
            // 独立逻辑，无上文
            // 作用：返回单字在高频词表中的位置序号，未收录字排在末尾
            return _charOrderRank.TryGetValue(c, out int rank) ? rank : int.MaxValue;
        }

        internal List<PhraseEntry> GetPhraseBucketSnapshot(char firstInitial)
        {
            // 与上文：读取 RegisterPhrase 按首字母分桶的词条列表
            // 作用：在锁内拷贝匹配首字母的词条快照供候选引擎无锁遍历
            lock (_phraseLock)
            {
                if (_phrasesByInitial.TryGetValue(firstInitial, out List<PhraseEntry>? bucket))
                {
                    return new List<PhraseEntry>(bucket);
                }
                return new List<PhraseEntry>(0);
            }
        }

        internal bool TryGetRegisteredPhrase(string word, out PhraseEntry? entry)
        {
            // 独立逻辑，无上文
            // 作用：按精确词形检索已注册词条元数据
            lock (_phraseLock)
            {
                return _phrasesByWord.TryGetValue(word, out entry);
            }
        }

        internal PhraseEntry? TryBuildPhraseEntry(string word, int baseWeight)
        {
            // 与上文：依赖 _charToPinyins 展开词组中各汉字的多音字读音
            // 作用：为词条生成上限 8 条全拼组合、音节序列与首字母简拼串
            List<string[]> charSyllableOptions = new List<string[]>(word.Length);
            for (int i = 0; i < word.Length; i++)
            {
                char ch = word[i];
                if (_charToPinyins.TryGetValue(ch, out string[]? pys) && pys.Length > 0)
                {
                    int take = Math.Min(2, pys.Length);
                    string[] slice = new string[take];
                    Array.Copy(pys, slice, take);
                    charSyllableOptions.Add(slice);
                }
            }

            if (charSyllableOptions.Count == 0)
            {
                return null;
            }

            const int maxPaths = 8;
            List<string[]> paths = new List<string[]>(maxPaths) { Array.Empty<string>() };

            for (int i = 0; i < charSyllableOptions.Count; i++)
            {
                string[] opts = charSyllableOptions[i];
                List<string[]> nextPaths = new List<string[]>(maxPaths);
                for (int p = 0; p < paths.Count && nextPaths.Count < maxPaths; p++)
                {
                    string[] prefix = paths[p];
                    for (int o = 0; o < opts.Length && nextPaths.Count < maxPaths; o++)
                    {
                        string[] extended = new string[prefix.Length + 1];
                        Array.Copy(prefix, extended, prefix.Length);
                        extended[prefix.Length] = opts[o];
                        nextPaths.Add(extended);
                    }
                }
                paths = nextPaths;
            }

            string[] fullPinyins = new string[paths.Count];
            string[][] syllableLists = new string[paths.Count][];
            string[] initialsList = new string[paths.Count];

            for (int i = 0; i < paths.Count; i++)
            {
                string[] syls = paths[i];
                syllableLists[i] = syls;
                fullPinyins[i] = string.Concat(syls);
                char[] initials = new char[syls.Length];
                for (int k = 0; k < syls.Length; k++)
                {
                    initials[k] = syls[k][0];
                }
                initialsList[i] = new string(initials);
            }

            return new PhraseEntry(word, baseWeight, fullPinyins, syllableLists, initialsList);
        }

        private void RegisterStandardSyllables()
        {
            // 独立逻辑，无上文
            // 作用：将标准普通话全音节表及其非空前缀写入合法检索集合
            for (int i = 0; i < StandardMandarinSyllables.Length; i++)
            {
                AddValidSyllable(StandardMandarinSyllables[i]);
            }
        }

        private void AddValidSyllable(string syllable)
        {
            // 与上文：服务于 RegisterStandardSyllables 与 LoadEmbeddedCharTable
            // 作用：注册合法音节并展开写入所有前缀到 _syllablePrefixes
            if (string.IsNullOrEmpty(syllable))
            {
                return;
            }

            _validSyllables.Add(syllable);
            for (int len = 1; len <= syllable.Length; len++)
            {
                _syllablePrefixes.Add(syllable.Substring(0, len));
            }
        }

        private void LoadEmbeddedCharTable()
        {
            // 与上文：依赖程序集内嵌资源 RefinedStorage.Assets.pinyin_chars.txt
            // 作用：解析 8105 字表建立单字与音节的正排和倒排索引并记录高频序位
            Assembly asm = typeof(PinyinLexicon).Assembly;
            using Stream? stream = asm.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                return;
            }

            using StreamReader reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            int rank = 0;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                int tabIdx = line.IndexOf('\t');
                if (tabIdx <= 0 || tabIdx >= line.Length - 1)
                {
                    continue;
                }

                string charPart = line.Substring(0, tabIdx);
                if (charPart.Length != 1 || char.IsSurrogate(charPart[0]))
                {
                    continue;
                }

                char ch = charPart[0];
                if (_charToPinyins.ContainsKey(ch))
                {
                    continue;
                }

                string[] rawSyllables = line.Substring(tabIdx + 1).Split(',');
                List<string> validCharPys = new List<string>(rawSyllables.Length);
                for (int i = 0; i < rawSyllables.Length; i++)
                {
                    string py = NormalizeSyllableKey(rawSyllables[i]);
                    if (py.Length == 0 || validCharPys.Contains(py))
                    {
                        continue;
                    }

                    validCharPys.Add(py);
                    AddValidSyllable(py);

                    if (!_syllableToChars.TryGetValue(py, out List<char>? bucket))
                    {
                        bucket = new List<char>();
                        _syllableToChars[py] = bucket;
                    }
                    bucket.Add(ch);
                }

                if (validCharPys.Count > 0)
                {
                    _charToPinyins[ch] = validCharPys.ToArray();
                    _charOrderRank[ch] = rank++;
                }
            }
        }

        private void RegisterSeedPhrases()
        {
            // 与上文：在字表加载完毕后立即执行
            // 作用：将星露谷高频物品与分类词条种子以高权重注册进词库
            for (int i = 0; i < StardewSeedPhrases.Length; i++)
            {
                RegisterPhrase(StardewSeedPhrases[i].Word, StardewSeedPhrases[i].Weight);
            }
        }

        private bool ContainsKnownHanzi(string text)
        {
            // 独立逻辑，无上文
            // 作用：检测字符串中是否包含字表已收录汉字，过滤纯英文或数字条目
            for (int i = 0; i < text.Length; i++)
            {
                if (_charToPinyins.ContainsKey(text[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private List<string[]> ExtractHanziReadings(string text)
        {
            // 与上文：为 MatchesPinyinOrInitials 构建待匹配文本的拼音读音序列
            // 作用：提取已收录汉字的 v/u 归一化读音，遇未收录 CJK 汉字插入空数组阻断跨字误拼，忽略标点空白
            List<string[]> readings = new List<string[]>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (_charToPinyins.TryGetValue(ch, out string[]? pys))
                {
                    List<string> normalized = new List<string>(pys.Length);
                    for (int p = 0; p < pys.Length; p++)
                    {
                        string uForm = pys[p].Replace('v', 'u');
                        if (!normalized.Contains(uForm))
                        {
                            normalized.Add(uForm);
                        }
                    }
                    readings.Add(normalized.ToArray());
                }
                else if (ch >= '\u4E00' && ch <= '\u9FFF')
                {
                    readings.Add(Array.Empty<string>());
                }
            }
            return readings;
        }

        private static bool CanMatchFrom(List<string[]> hanziReadings, int charIdx, int queryPos, string cleanQuery, bool[] memoFailed)
        {
            // 与上文：由 MatchesPinyinOrInitials 从各个起始汉字位置调用
            // 作用：记忆化递归匹配当前位置之后的全拼、首字母简拼及末尾音节前缀
            if (queryPos >= cleanQuery.Length)
            {
                return true;
            }

            if (charIdx >= hanziReadings.Count)
            {
                return false;
            }

            int memoIdx = charIdx * cleanQuery.Length + queryPos;
            if (memoFailed[memoIdx])
            {
                return false;
            }

            string[] pys = hanziReadings[charIdx];
            if (pys.Length == 0)
            {
                memoFailed[memoIdx] = true;
                return false;
            }

            ReadOnlySpan<char> rem = cleanQuery.AsSpan(queryPos);
            for (int i = 0; i < pys.Length; i++)
            {
                string py = pys[i];
                if (rem.Length >= py.Length)
                {
                    if (rem.StartsWith(py.AsSpan(), StringComparison.Ordinal)
                        && CanMatchFrom(hanziReadings, charIdx + 1, queryPos + py.Length, cleanQuery, memoFailed))
                    {
                        return true;
                    }
                }
                else if (py.AsSpan().StartsWith(rem, StringComparison.Ordinal))
                {
                    return true;
                }

                if (rem[0] == py[0] && CanMatchFrom(hanziReadings, charIdx + 1, queryPos + 1, cleanQuery, memoFailed))
                {
                    return true;
                }
            }

            memoFailed[memoIdx] = true;
            return false;
        }

        private static string NormalizeSyllableKey(string? raw)
        {
            // 独立逻辑，无上文
            // 作用：提取音节中的纯小写英文字母用于精确音节键比较
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = char.ToLowerInvariant(raw[i]);
                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        private static string CleanQueryForMatching(string? asciiQuery)
        {
            // 独立逻辑，无上文
            // 作用：清洗搜索关键字为纯小写英文字母并将 v 归一化为 u 以兼容双拼输入
            if (string.IsNullOrWhiteSpace(asciiQuery))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(asciiQuery.Length);
            for (int i = 0; i < asciiQuery.Length; i++)
            {
                char ch = char.ToLowerInvariant(asciiQuery[i]);
                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch == 'v' ? 'u' : ch);
                }
            }
            return sb.ToString();
        }
    }
}
