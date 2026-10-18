using System;
using System.Collections.Generic;
using System.Text;

namespace Speech;

/// <summary>
/// English spelling to phonemes: the layer S.A.M. called RECITER.
///
/// <b>Context-sensitive rewrite rules, the 1976 NRL design.</b> Each rule says "in this
/// left context, these letters, in this right context, make these sounds", and the
/// first rule that matches at each position wins. Elovitz and colleagues at the Naval
/// Research Laboratory showed a few hundred such rules get most of English right, which
/// is remarkable given how little English spelling deserves it, and every hobby
/// synthesiser since has used the shape.
///
/// <b>What this is, precisely.</b> The <i>matcher</i> is the NRL design implemented
/// faithfully -- the context classes below are theirs. <b>The rule table is not the NRL
/// table.</b> It is a working set written for this project, covering the patterns that
/// carry ordinary English, and it is smaller than theirs and certainly less good. It is
/// hand-written and hand-checked rather than transcribed from the report, so treat its
/// output as a starting point you edit, not as an authority -- which is exactly why the
/// UI derives phonemes into an editable box rather than speaking English directly.
/// The self-test scores it against known pronunciations so the quality is a measured
/// number rather than a claim.
///
/// <b>Irregular words are a dictionary, not rules.</b> No rule set reaches "colonel" or
/// "one", and pretending otherwise means contorting rules that then break regular
/// words. <see cref="Exceptions"/> is checked first, whole-word.
/// </summary>
public static class LetterToSound
{
    /// <summary>
    /// One rewrite rule.
    ///
    /// Contexts use the NRL classes, which are what make a few hundred rules cover a
    /// language rather than a few thousand:
    /// <list type="bullet">
    /// <item><c>#</c> one or more vowels</item>
    /// <item><c>:</c> zero or more consonants</item>
    /// <item><c>^</c> one consonant</item>
    /// <item><c>.</c> one voiced consonant</item>
    /// <item><c>+</c> one front vowel (E, I, Y)</item>
    /// <item><c>%</c> a suffix: E, ES, ED, ER, ING, ELY -- the class that makes silent-e
    /// work, because it is what tells MAKE and MAKING from ANIMAL</item>
    /// <item><c>&amp;</c> a sibilant</item>
    /// <item><c>@</c> a consonant that swallows the Y in a following long U</item>
    /// <item><c>' '</c> a word boundary</item>
    /// </list>
    /// </summary>
    private readonly record struct Rule(string Left, string Focus, string Right, string Phonemes);

    private static Rule R(string left, string focus, string right, string phonemes)
        => new(left, focus, right, phonemes);

    // --- Character classes -------------------------------------------------

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';
    private static bool IsLetter(char c) => c >= 'A' && c <= 'Z';
    private static bool IsConsonant(char c) => IsLetter(c) && !IsVowel(c);
    private static bool IsFront(char c) => c is 'E' or 'I' or 'Y';
    private static bool IsSibilant(char c) => c is 'S' or 'C' or 'G' or 'Z' or 'X' or 'J';
    private static bool IsVoiced(char c) => c is 'B' or 'D' or 'V' or 'G' or 'J' or 'L'
                                                 or 'M' or 'N' or 'R' or 'W' or 'Z';

    /// <summary>Consonants after which a long U loses its Y glide -- "rule" and "tune",
    /// not "rye-ool" and "tyoon".</summary>
    private static bool IsYSwallowing(char c) => c is 'T' or 'S' or 'R' or 'D' or 'L'
                                                      or 'Z' or 'N' or 'J';

    /// <summary>Longest suffix matching at a position, or 0. Order matters: ING before
    /// E, or "MAKING" would match the E class and stop early.</summary>
    private static readonly string[] Suffixes = { "ING", "ELY", "ED", "ER", "ES", "E" };

    /// <summary>
    /// A suffix here, or 0.
    ///
    /// <b>It must end the word, and forgetting that was a real bug.</b> Without the
    /// check, the one-letter suffix "E" matched the E of "SEVEN" and reported a suffix
    /// where there was only a letter that happened to be one -- so the silent-E rule
    /// fired and SEVEN came out "S IY V EH N". A suffix is by definition what a word
    /// ends with; matching it mid-word makes the whole class meaningless, since almost
    /// every word contains an E somewhere.
    /// </summary>
    private static int SuffixLength(string text, int at)
    {
        foreach (string suffix in Suffixes)
        {
            if (at + suffix.Length > text.Length) continue;
            if (string.CompareOrdinal(text, at, suffix, 0, suffix.Length) != 0) continue;

            int after = at + suffix.Length;
            if (after < text.Length && IsLetter(text[after])) continue;

            return suffix.Length;
        }
        return 0;
    }

    // --- Matching ----------------------------------------------------------

    /// <summary>
    /// Matches a right context forwards from <paramref name="at"/>.
    ///
    /// The text is space-padded by the caller, so an index never runs off either end and
    /// a boundary genuinely reads as a boundary rather than as an exception.
    /// </summary>
    private static bool MatchesRight(string text, int at, string pattern)
    {
        int i = at;
        foreach (char c in pattern)
        {
            if (i >= text.Length) return false;

            switch (c)
            {
                case '#':
                    if (!IsVowel(text[i])) return false;
                    while (i < text.Length && IsVowel(text[i])) i++;
                    break;
                case ':':
                    while (i < text.Length && IsConsonant(text[i])) i++;
                    break;
                case '^':
                    if (!IsConsonant(text[i])) return false;
                    i++;
                    break;
                case '.':
                    if (!IsVoiced(text[i])) return false;
                    i++;
                    break;
                case '+':
                    if (!IsFront(text[i])) return false;
                    i++;
                    break;
                case '&':
                    if (!IsSibilant(text[i])) return false;
                    i++;
                    break;
                case '@':
                    if (!IsYSwallowing(text[i])) return false;
                    i++;
                    break;
                case '%':
                    int length = SuffixLength(text, i);
                    if (length == 0) return false;
                    i += length;
                    break;
                case ' ':
                    if (IsLetter(text[i])) return false;
                    i++;
                    break;
                default:
                    if (text[i] != c) return false;
                    i++;
                    break;
            }
        }
        return true;
    }

    /// <summary>Matches a left context backwards from just before <paramref name="at"/>.
    /// The pattern is written left to right as it appears in the text, so it is walked
    /// in reverse.</summary>
    private static bool MatchesLeft(string text, int at, string pattern)
    {
        int i = at - 1;
        for (int p = pattern.Length - 1; p >= 0; p--)
        {
            if (i < 0) return false;

            switch (pattern[p])
            {
                case '#':
                    if (!IsVowel(text[i])) return false;
                    while (i >= 0 && IsVowel(text[i])) i--;
                    break;
                case ':':
                    while (i >= 0 && IsConsonant(text[i])) i--;
                    break;
                case '^':
                    if (!IsConsonant(text[i])) return false;
                    i--;
                    break;
                case '.':
                    if (!IsVoiced(text[i])) return false;
                    i--;
                    break;
                case '+':
                    if (!IsFront(text[i])) return false;
                    i--;
                    break;
                case '&':
                    if (!IsSibilant(text[i])) return false;
                    i--;
                    break;
                case '@':
                    if (!IsYSwallowing(text[i])) return false;
                    i--;
                    break;
                case ' ':
                    if (IsLetter(text[i])) return false;
                    i--;
                    break;
                default:
                    if (text[i] != pattern[p]) return false;
                    i--;
                    break;
            }
        }
        return true;
    }

    // --- Translation -------------------------------------------------------

    /// <summary>
    /// Turns English into a phoneme string.
    ///
    /// Words are translated independently and joined with spaces -- <b>no pauses between
    /// them</b>, because connected speech has none and a gap at every word boundary
    /// sounds like a station announcement. Punctuation that ends a clause becomes a
    /// pause, which is the one place a gap belongs.
    ///
    /// <paramref name="guessed"/> collects the words that went through the rules rather
    /// than the exception dictionary, so a caller can say which parts of a transcription
    /// are the risky ones.
    /// </summary>
    public static string Translate(string english, out List<string> guessed)
    {
        guessed = new List<string>();
        var output = new List<string>();
        if (string.IsNullOrWhiteSpace(english)) return "";

        var tokens = new List<string>(Tokenise(english));

        // A word alone is not a sentence, and nothing in it can be relatively unstressed.
        // "The" said by itself is a word being pronounced; "the" in a sentence is a
        // function word getting out of the way.
        int words = 0;
        foreach (string token in tokens) if (token != "-") words++;
        bool connected = words > 1;

        foreach (string token in tokens)
        {
            if (token == "-") { output.Add("-"); continue; }

            bool inDictionary = Exceptions.TryGetValue(token, out string known);
            string phonemes = inDictionary ? known : TranslateWord(token);
            if (phonemes.Length == 0) continue;

            var entry = Phonemes.Parse(phonemes, out _);

            // Three different treatments, and the order they are tested in matters
            // because most function words are also dictionary entries.
            List<Phoneme> shaped;
            if (connected && Function.Contains(token))
            {
                // Function words carry no sentence accent. This is the level above word
                // stress -- which syllable of a word is strongest is a property of the
                // word, but whether the word itself is prominent is a property of the
                // sentence, and articles and prepositions never are.
                shaped = Prosody.Unstress(entry);
            }
            else if (inDictionary)
            {
                shaped = Prosody.Apply(entry, reduce: false);
            }
            else
            {
                // The rules produce full vowels everywhere and genuinely need reduction.
                shaped = Prosody.Apply(entry, reduce: true);
                guessed.Add(token);
            }

            output.Add(Phonemes.Format(shaped));
        }

        // A pause at either end is silence bolted onto silence -- a sentence-final full
        // stop is real punctuation but there is nothing after it to separate from.
        while (output.Count > 0 && output[0] == "-") output.RemoveAt(0);
        while (output.Count > 0 && output[output.Count - 1] == "-") output.RemoveAt(output.Count - 1);

        return string.Join(" ", output);
    }

    /// <summary>Splits into words, turning clause-ending punctuation into a pause and
    /// discarding the rest. An apostrophe stays inside a word, because "don't" is one
    /// word and splitting it makes two unpronounceable ones.</summary>
    private static IEnumerable<string> Tokenise(string english)
    {
        var word = new StringBuilder();

        foreach (char raw in english)
        {
            char c = char.ToUpperInvariant(raw);

            if (IsLetter(c) || c == '\'')
            {
                word.Append(c);
                continue;
            }

            if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
            if (c is ',' or '.' or ';' or ':' or '?' or '!' or '-') yield return "-";
        }

        if (word.Length > 0) yield return word.ToString();
    }

    /// <summary>Applies the rules across one word, first match wins at each position.</summary>
    private static string TranslateWord(string word)
    {
        // Padded, so a rule asking for a word boundary finds one rather than an edge.
        string text = " " + word + " ";
        var output = new List<string>();

        int at = 1;
        while (at < text.Length - 1)
        {
            char letter = text[at];
            if (!IsLetter(letter)) { at++; continue; }

            bool matched = false;
            foreach (var rule in RulesFor(letter))
            {
                if (!MatchesRight(text, at, rule.Focus)) continue;
                if (!MatchesLeft(text, at, rule.Left)) continue;
                if (!MatchesRight(text, at + rule.Focus.Length, rule.Right)) continue;

                if (rule.Phonemes.Length > 0) output.Add(rule.Phonemes);
                at += rule.Focus.Length;
                matched = true;
                break;
            }

            // Cannot happen while every letter keeps its catch-all rule, but skipping is
            // the right failure: one dropped letter beats an infinite loop.
            if (!matched) at++;
        }

        return string.Join(" ", output);
    }

    private static IReadOnlyList<Rule> RulesFor(char letter)
        => ByLetter.TryGetValue(letter, out var rules) ? rules : Array.Empty<Rule>();

    private static Dictionary<char, List<Rule>> Index()
    {
        var map = new Dictionary<char, List<Rule>>();
        foreach (var rule in Rules)
        {
            char key = rule.Focus[0];
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Rule>();
            list.Add(rule);
        }
        return map;
    }

    // --- The rules ---------------------------------------------------------
    //
    // Order is everything: the first match wins, so specific rules precede general ones
    // and every letter ends with a catch-all. Within a letter they run longest and most
    // contextual first.

    private static readonly Rule[] Rules =
    {
        // --- A ---
        R(" ", "ARE", " ", "AA R"),
        R("", "AIR", "", "EH R"),
        R("", "AI", "", "EY"),
        R("", "AY", "", "EY"),
        R("", "AU", "", "AO"),
        R("", "AW", "", "AO"),
        R("", "ALK", "", "AO K"),
        R("", "ALL", "", "AO L"),
        R("", "AL", "^", "AO L"),
        R("", "ARR", "", "AE R"),
        R("", "AR", "", "AA R"),
        // Anchored to the word end on purpose: NATION and STATION take the long vowel,
        // NATIONAL does not, and the difference is stress, which no rule here can see.
        R("", "A", "TION ", "EY"),
        R("", "A", "^%", "EY"),          // MAKE, MAKING, GATES -- but not ANIMAL
        R("", "A", "^^", "AE"),
        R("", "A", "", "AE"),

        // --- B ---
        R("", "BB", "", "B"),
        R("", "B", "", "B"),

        // --- C ---
        R("", "CH", "", "CH"),
        R("", "CK", "", "K"),
        R("", "CC", "+", "K S"),
        R("", "CC", "", "K"),
        R("", "CI", "A", "SH"),
        R("", "CI", "O", "SH"),
        R("", "C", "+", "S"),            // CITY, CENT, CYCLE
        R("", "C", "", "K"),

        // --- D ---
        R("#:^", "ED", " ", "T"),        // WALKED, JUMPED
        R("", "DGE", "", "JH"),          // BRIDGE, JUDGE -- the D is not sounded
        R("", "DD", "", "D"),
        R("", "D", "", "D"),

        // --- E ---
        R("#:", "E", " ", ""),           // silent final E: MAKE, GATE
        R("", "EE", "", "IY"),
        R("", "EIGH", "", "EY"),
        R("", "EA", "R^", "ER"),         // EARTH, HEARD
        R("", "EAR", "", "IH R"),        // EAR, HEAR, NEAR
        R("", "EA", "", "IY"),
        R("", "EI", "", "IY"),
        R("", "EW", "", "Y UW"),
        R("", "EY", "", "IY"),
        R("", "ER", "", "ER"),
        R("", "E", "^%", "IY"),          // HERE, THESE
        R("", "E", " ", "IY"),           // HE, SHE, BE, ME, WE
        R("", "E", "", "EH"),

        // --- F ---
        R("", "FF", "", "F"),
        R("", "F", "", "F"),

        // --- G ---
        R("", "GG", "", "G"),
        R("", "GH", "", ""),             // silent: NIGHT, THOUGH
        R("", "G", "+", "JH"),           // GEM, GIN, GYM
        R("", "G", "", "G"),

        // --- H ---
        R("", "H", "#", "HH"),
        R("", "H", "", ""),              // silent elsewhere

        // --- I ---
        R("", "IGH", "", "AY"),
        R("", "ILD", " ", "AY L D"),
        R("", "IND", " ", "AY N D"),     // FIND, MIND, KIND
        R("", "IE", "", "IY"),
        R("", "IR", "", "ER"),
        R("", "I", "^%", "AY"),          // TIME, LIKE, WRITING
        R("", "I", "^^", "IH"),
        R("", "I", "", "IH"),

        // --- J ---
        R("", "J", "", "JH"),

        // --- K ---
        R(" ", "K", "N", ""),            // KNIFE, KNOW
        R("", "KK", "", "K"),
        R("", "K", "", "K"),

        // --- L ---
        R("", "LL", "", "L"),
        R("", "L", "", "L"),

        // --- M ---
        R("", "MB", " ", "M"),           // CLIMB, THUMB
        R("", "MM", "", "M"),
        R("", "M", "", "M"),

        // --- N ---
        R("", "NGL", "%", "NG G AH L"),
        R("", "NG", "+", "N JH"),
        R("", "NK", "", "NG K"),         // THINK, BANK
        R("", "NG", "", "NG"),
        R("", "NN", "", "N"),
        R("", "N", "", "N"),

        // --- O ---
        R("", "OUGH", "T", "AO"),        // THOUGHT, BOUGHT
        // The K is inside the focus, not the right context. As a context it was matched
        // but not consumed, so BOOK came out "B UH K K" -- a rule that emits a sound for
        // a letter it does not advance past will always double it.
        R("", "OOK", "", "UH K"),        // BOOK, LOOK, TOOK
        // No OOD rule: "good" and "food" disagree and nothing in the spelling says
        // which, so the commoner UW is the default and the short ones are exceptions.
        R("", "OO", "", "UW"),           // MOON, SOON, FOOD
        R("", "OUL", "D", "UH L"),       // WOULD, COULD, SHOULD
        R("", "OU", "", "AW"),
        R("", "OW", "", "OW"),
        R("", "OI", "", "OY"),
        R("", "OY", "", "OY"),
        R("", "OR", "", "AO R"),
        R("", "OA", "", "OW"),
        R("", "O", "^%", "OW"),          // HOPE, NOTE, SMOKING
        R("", "O", "^ ", "AA"),          // DOG, HOT, NOT
        R("", "O", "^^", "AA"),
        R("", "O", "", "OW"),

        // --- P ---
        R("", "PH", "", "F"),
        R("", "PP", "", "P"),
        R("", "P", "", "P"),

        // --- Q ---
        R("", "QU", "", "K W"),
        R("", "Q", "", "K"),

        // --- R ---
        R("", "RR", "", "R"),
        R("", "R", "", "R"),

        // --- S ---
        R("", "SH", "", "SH"),
        R("", "SION", "", "SH AH N"),
        R("", "SS", "", "S"),
        R("#", "S", " ", "Z"),           // plural after a vowel: DOGS, KEYS
        R(".", "S", " ", "Z"),           // and after a voiced consonant
        R("", "S", "", "S"),

        // --- T ---
        R("", "TION", "", "SH AH N"),    // NATION, STATION
        R("", "TIAL", "", "SH AH L"),
        R("", "TCH", "", "CH"),
        R("", "TH", "", "TH"),
        R("", "TT", "", "T"),
        R("", "T", "", "T"),

        // --- U ---
        R("", "UY", "", "AY"),
        R("", "UR", "", "ER"),
        R("@", "U", "^%", "UW"),         // RULE, TUNE -- no Y glide after T S R D L Z N J
        R("", "U", "^%", "Y UW"),        // CUTE, MUSE, HUGE
        R("", "U", "", "AH"),

        // --- V ---
        R("", "V", "", "V"),

        // --- W ---
        R("", "WR", "", "R"),            // WRITE, WRONG
        R("", "WHO", "", "HH UW"),
        R("", "WH", "", "W"),
        R("", "WOR", "^", "W ER"),       // WORD, WORK, WORLD
        R("", "WAR", "", "W AO R"),
        R("", "WA", "^", "W AA"),        // WANT, WATCH, WASH
        R("", "W", "", "W"),

        // --- X ---
        R("", "X", "", "K S"),

        // --- Y ---
        R(" ", "Y", "", "Y"),            // YES, YOU -- consonantal at the start
        R("#:^", "Y", " ", "IY"),        // CITY, HAPPY, MANY
        R("", "Y", "^%", "AY"),          // TYPE, STYLE
        R("^", "Y", " ", "AY"),          // MY, BY, WHY, SHY
        R("", "Y", "", "IH"),

        // --- Z ---
        R("", "ZZ", "", "Z"),
        R("", "Z", "", "Z"),
    };

    /// <summary>
    /// Rules bucketed by the first letter of their focus, so a lookup is one dictionary
    /// hit rather than a scan of the whole table.
    ///
    /// <b>Declared after <see cref="Rules"/>, and it has to be.</b> Static field
    /// initialisers run in declaration order, so with this above the table it indexed a
    /// null array and the whole class failed to initialise -- reported, unhelpfully, as
    /// a TypeInitializationException wrapping a NullReferenceException the first time
    /// anything asked it to translate a word.
    /// </summary>
    private static readonly Dictionary<char, List<Rule>> ByLetter = Index();

    // --- Exceptions --------------------------------------------------------

    /// <summary>
    /// Words no rule set reaches, checked before the rules.
    ///
    /// <b>Mostly the highest-frequency words in English</b>, which is not a coincidence:
    /// the words used most are the ones spelling reform never touched, so "one", "of",
    /// "said" and "women" stayed irregular precisely by being common. A short dictionary
    /// weighted this way buys far more accuracy per entry than any rule could.
    ///
    /// The names at the end are here so the shipped examples work; they are not a claim
    /// to general coverage of proper nouns, which is its own problem and a large one.
    /// </summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        // Function words
        ["A"] = "AH",           ["THE"] = "DH AH",      ["OF"] = "AH V",
        ["TO"] = "T UW",        ["AND"] = "AE N D",     ["IS"] = "IH Z",
        ["WAS"] = "W AA Z",     ["ARE"] = "AA R",       ["WERE"] = "W ER",
        ["YOU"] = "Y UW",       ["YOUR"] = "Y AO R",    ["I"] = "AY",
        ["MY"] = "M AY",        ["ME"] = "M IY",        ["HE"] = "HH IY",
        ["SHE"] = "SH IY",      ["WE"] = "W IY",        ["THEY"] = "DH EY",
        ["THEM"] = "DH EH M",   ["THIS"] = "DH IH S",   ["THAT"] = "DH AE T",
        ["THESE"] = "DH IY Z",  ["THOSE"] = "DH OW Z",  ["THERE"] = "DH EH R",
        ["THEIR"] = "DH EH R",  ["THEN"] = "DH EH N",   ["THAN"] = "DH AE N",
        ["WITH"] = "W IH DH",   ["WHAT"] = "W AH T",    ["WHEN"] = "W EH N",
        ["WHERE"] = "W EH R",   ["WHY"] = "W AY",       ["WHO"] = "HH UW",
        ["HOW"] = "HH AW",      ["NOT"] = "N AA T",     ["NO"] = "N OW",
        ["YES"] = "Y EH S",     ["FOR"] = "F AO R",     ["FROM"] = "F R AH M",
        ["AS"] = "AE Z",        ["HAS"] = "HH AE Z",    ["HAVE"] = "HH AE V",
        ["HAD"] = "HH AE D",    ["BE"] = "B IY",        ["BEEN"] = "B IH N",
        ["DO"] = "D UW",        ["DOES"] = "D AH Z",    ["DONE"] = "D AH N",
        ["ONE"] = "W AH N",     ["TWO"] = "T UW",       ["ONCE"] = "W AH N S",
        ["SAID"] = "S EH D",    ["SAYS"] = "S EH Z",    ["AGAIN"] = "AH G EH N",
        ["ANY"] = "EH N IY",    ["MANY"] = "M EH N IY", ["SOME"] = "S AH M",
        ["COME"] = "K AH M",    ["GONE"] = "G AO N",    ["GIVE"] = "G IH V",
        ["LIVE"] = "L IH V",    ["LOVE"] = "L AH V",    ["MOVE"] = "M UW V",
        ["WANT"] = "W AA N T",  ["WATER"] = "W AO T ER",["WORD"] = "W ER D",
        ["WORK"] = "W ER K",    ["WORLD"] = "W ER L D", ["WOULD"] = "W UH D",
        // The short OO words, which the default long OO gets wrong.
        ["WOOD"] = "W UH D",    ["STOOD"] = "S T UH D", ["HOOD"] = "HH UH D",
        ["FOOT"] = "F UH T",    ["WOOL"] = "W UH L",
        ["COULD"] = "K UH D",   ["SHOULD"] = "SH UH D", ["ABOUT"] = "AH B AW T",
        ["AFRAID"] = "AH F R EY D",                     ["CANNOT"] = "K AE N AA T",
        ["PEOPLE"] = "P IY P AH L",                     ["FRIEND"] = "F R EH N D",
        ["WOMAN"] = "W UH M AH N",                      ["WOMEN"] = "W IH M AH N",
        ["BUSY"] = "B IH Z IY",                         ["EYE"] = "AY",
        ["ISLAND"] = "AY L AH N D",                     ["COLONEL"] = "K ER N AH L",
        ["ENOUGH"] = "IH N AH F",                       ["LAUGH"] = "L AE F",
        ["THROUGH"] = "TH R UW", ["THOUGH"] = "DH OW",  ["THOUGHT"] = "TH AO T",
        ["MOTHER"] = "M AH DH ER",                      ["FATHER"] = "F AA DH ER",
        ["OTHER"] = "AH DH ER", ["ANOTHER"] = "AH N AH DH ER",
        ["OPEN"] = "OW P AH N", ["ONLY"] = "OW N L IY", ["OVER"] = "OW V ER",
        ["HELLO"] = "HH AH L OW",                       ["GOOD"] = "G UH D",
        ["MORNING"] = "M AO R N IH NG",                 ["AFTERNOON"] = "AE F T ER N UW N",
        ["COMPUTER"] = "K AH M P Y UW T ER",            ["MACHINE"] = "M AH SH IY N",
        ["GAME"] = "G EY M",    ["PLAY"] = "P L EY",    ["LIKE"] = "L AY K",
        ["NAME"] = "N EY M",    ["SORRY"] = "S AA R IY",["DOOR"] = "D AO R",
        ["DOORS"] = "D AO R Z", ["BAY"] = "B EY",       ["POD"] = "P AA D",

        // Daisy Bell's chorus. Contractions are single tokens because the tokeniser
        // keeps apostrophes inside words -- splitting "won't" gives two unpronounceable
        // halves rather than two words.
        ["HALF"] = "HH AE F",   ["CRAZY"] = "K R EY Z IY",
        ["ANSWER"] = "AE N S ER",                       // the W is silent
        ["STYLISH"] = "S T AY L IH SH",                 ["MARRIAGE"] = "M EH R IH JH",
        ["CARRIAGE"] = "K EH R IH JH",                  ["AFFORD"] = "AH F AO R D",
        ["BICYCLE"] = "B AY S IH K AH L",               ["UPON"] = "AH P AA N",
        ["I'M"] = "AY M",       ["WON'T"] = "W OW N T", ["CAN'T"] = "K AE N T",
        ["YOU'LL"] = "Y UW L",  ["MADE"] = "M EY D",    ["BUILT"] = "B IH L T",

        // Lavender's Blue. BLUE and SHALL are here because the rules get them wrong
        // rather than merely unhandled: "blue" has no consonant between its U and its
        // silent E so the long-U rule cannot fire and it comes out "B L AH", and
        // "shall" trips the ALL rule and comes out rhyming with "hall".
        ["BLUE"] = "B L UW",    ["SHALL"] = "SH AE L",  ["DILLY"] = "D IH L IY",
        ["LAVENDER"] = "L AE V AH N D ER",
        ["LAVENDER'S"] = "L AE V AH N D ER Z",

        // Proper nouns used by the shipped examples. Not a claim to general coverage.
        ["DAVE"] = "D EY V",    ["STEPHEN"] = "S T IY V AH N",
        ["HAWKING"] = "HH AO K IH NG",                  ["DAISY"] = "D EY Z IY",
        ["POKEY"] = "P OW K IY",["ATARI"] = "AH T AA R IY",
    };

    /// <summary>
    /// Words that carry no sentence accent.
    ///
    /// <b>Deliberately conservative, and two omissions are the point.</b> "That" and
    /// "this" are on every list of English function words and are not on this one,
    /// because both are also demonstratives that take full stress -- "do <i>that</i>" --
    /// and flattening them where they are stressed is a worse error than leaving them
    /// prominent where they are not. Nothing here can tell the two uses apart without a
    /// parser, so the ambiguous cases are left alone.
    /// </summary>
    private static readonly HashSet<string> Function = new(StringComparer.Ordinal)
    {
        "A", "AN", "THE", "OF", "TO", "AND", "OR", "BUT", "IF", "AS",
        "IN", "ON", "AT", "BY", "FOR", "FROM", "WITH", "INTO", "UPON",
        "IS", "WAS", "ARE", "WERE", "BE", "BEEN", "AM", "BEING",
        "DO", "DOES", "DID", "HAS", "HAVE", "HAD",
        "WILL", "WOULD", "CAN", "COULD", "SHALL", "SHOULD", "MAY", "MIGHT",
    };

    /// <summary>Whether a word is in the exception dictionary rather than going through
    /// the rules -- what the UI uses to mark which parts of a transcription are
    /// guesses.</summary>
    public static bool IsKnown(string word) => Exceptions.ContainsKey(word.ToUpperInvariant());

    public static int ExceptionCount => Exceptions.Count;
    public static int RuleCount => Rules.Length;
}
