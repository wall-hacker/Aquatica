namespace Aquatica.Expedition
{
    public enum CrateContentKind { Log, Crystal, Artifact }

    /// <summary>What came out of a crate: a page of the expedition's logs, a Tide Crystal or an artifact.</summary>
    public struct CrateContent
    {
        public CrateContentKind kind;
        /// <summary>Which log / crystal / artifact (0-based, in the order they are found).</summary>
        public int index;
        public string title;
        public string text;
    }

    /// <summary>
    /// The lost expedition's story: what the crates hold, in the order the player finds it, whatever route they take.
    /// Kept short and vague on purpose (the cockpit monitor shows 17 characters by 5 lines per page).
    /// </summary>
    public static class ExpeditionStory
    {
        public const int CrystalCount = 3;

        /// <summary>Contents of the 1st, 2nd, ... crate brought up.</summary>
        public static readonly CrateContentKind[] Order =
        {
            CrateContentKind.Log, CrateContentKind.Log, CrateContentKind.Crystal, CrateContentKind.Log, CrateContentKind.Artifact,
            CrateContentKind.Crystal, CrateContentKind.Log, CrateContentKind.Crystal, CrateContentKind.Artifact, CrateContentKind.Artifact,
        };

        /// <summary>Logs found in crates; the last entry plays when the gateway opens.</summary>
        public static readonly (string title, string text)[] Logs =
        {
            ("LOG 03 - DAY 3", "SEABED IS 4 DEGREES WARMER THAN THE SURFACE. CHECKED THE SENSORS TWICE. NO VENTS ON ANY CHART."),
            ("MANIFEST, CRATE 7", "3X CRYSTAL, RECOVERED NEAR THE BIG ARCH. DO NOT STORE TOGETHER. (UNDERLINED TWICE. NOT MY HAND.)"),
            ("LOG 09 - DAY 11", "EVERY ARCH HAS THE SAME SPAN, TO THE CENTIMETRE. HALE SAYS COINCIDENCE. HALE MEASURED THEM TWICE."),
            ("LOG 14 - DAY 16", "SONAR PICKS UP A KNOCK AT NIGHT. REGULAR. NOT OUR HULL. HALE HAS STARTED COUNTING THEM."),
        };

        public static readonly (string title, string text) FinalLog =
            ("LAST ENTRY", "HALE TOOK THE CRYSTALS TO THE GATE. WE SPLIT THEM UP AND BURIED THEM. IF YOU READ THIS, YOU FOUND THEM TOO. SO DID WE. TELL THEM WE TR");

        public static int TotalLogs => Logs.Length + 1;

        public static readonly (string title, string text)[] Artifacts =
        {
            ("GOLD COINS", "FOURTEEN COINS, ALL WORN SMOOTH. NO TWO FROM THE SAME MINT."),
            ("POCKET WATCH", "SILVER, ENGRAVED E.H. STOPPED AT 03:12. THE GLASS IS UNBROKEN."),
            ("JADE FIGURE", "SMALL, KNEELING, HANDS RAISED. NO MAKER'S MARK. FOUND FACING NORTH."),
        };

        public static readonly (string title, string text) Crystal =
            ("TIDE CRYSTAL", "COLD TO THE TOUCH. THE GATE'S SOCKET READING JUMPED WHEN IT CAME ABOARD.");

        public static CrateContent Make(CrateContentKind kind, int index)
        {
            var (title, text) = kind switch
            {
                CrateContentKind.Log => Logs[index],
                CrateContentKind.Artifact => Artifacts[index],
                _ => Crystal,
            };
            return new CrateContent { kind = kind, index = index, title = title, text = text };
        }
    }
}
