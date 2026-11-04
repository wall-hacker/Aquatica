using System.Collections.Generic;
using Aquatica.Cockpit;
using Aquatica.Navigation;
using UnityEngine;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The expedition's pages on the navigation station's screens. The console offers the magnet when a crate is in
    /// reach (or says what's missing). The monitor scans a recovered crate, reveals what was inside and pages through
    /// its text (tap the monitor to turn the page), and the status page counts crystals, logs and artifacts.
    /// </summary>
    public class ExpeditionScreens : IStationOverlay
    {
        enum MonitorMode { Idle, Scanning, Reading }

        const float ScanTime = 2.6f;
        const float PageTime = 7f;
        const int BodyChars = 17;
        const int BodyLines = 4;
        static readonly Color CrystalColor = new(0.35f, 1.1f, 1.6f);
        static readonly Color ArtifactColor = new(1.5f, 1.1f, 0.3f);

        readonly ExpeditionDirector director;
        MonitorMode mode;
        float modeTime;
        CrateContent content;
        string header;
        readonly List<string[]> pages = new();
        int page;
        float pageTime;
        int pagesShown;
        System.Action revealed;
        CockpitScreen tappableMonitor;
        float lastDraw;

        public ExpeditionScreens(ExpeditionDirector director) => this.director = director;

        public bool Reading => mode != MonitorMode.Idle;

        /// <summary>Scan a recovered crate, then show what was in it. onRevealed runs when the scan finishes.</summary>
        public void ShowCrate(CrateContent crateContent, string pageHeader, System.Action onRevealed)
        {
            content = crateContent;
            header = pageHeader;
            revealed = onRevealed;
            mode = MonitorMode.Scanning;
            modeTime = 0f;
        }

        /// <summary>Show a text straight away (no scan), e.g. the last log when the gate opens.</summary>
        public void ShowText(CrateContent text, string pageHeader)
        {
            content = text;
            header = pageHeader;
            revealed = null;
            BeginReading();
        }

        public string[] StatusLines => new[]
        {
            $"CRYSTALS {director.Crystals}/{ExpeditionStory.CrystalCount}",
            $"LOGS {director.Logs}/{ExpeditionStory.TotalLogs} ART {director.Artifacts}/{ExpeditionStory.Artifacts.Length}",
        };

        public bool ConsoleTapped() => director.TryEngageMagnet();

        // ---------------------------------------------------------------- console

        public bool DrawConsole(CockpitScreen s)
        {
            var crate = director.NearestCrate;
            if (crate == null || director.MagnetBusy)
                return false;
            float distance = director.NearestCrateDistance;
            bool ready = director.CanReach(crate, out string hint);
            if (!ready && distance > 30f)
                return false;

            int cx = s.Width / 2;
            s.Begin();
            if (Blink(1.6f, 0.7f))
                s.Text("CRATE DETECTED", cx, 6, TextAlign.Center);
            s.Rule(18);
            if (ready)
            {
                float k = Blink(1.1f, 0.6f) ? 1f : 0.55f;
                int width = PixelFont.Measure("SALVAGE MAGNET") + 14;
                s.Rect(cx - width / 2, 26, width, 30, k);
                s.Rect(cx - width / 2 + 2, 28, width - 4, 26, k * 0.4f);
                s.Text("ENGAGE", cx, 32, TextAlign.Center, 1, k);
                s.Text("SALVAGE MAGNET", cx, 43, TextAlign.Center, 1, k);
                s.Text("TAP SCREEN", cx, 70, TextAlign.Center, 1, 0.45f);
            }
            else
            {
                s.Text($"RANGE {distance:0} M", cx, 28, TextAlign.Center, 1, 0.8f);
                if (Blink(1.2f, 0.7f))
                    s.Text(hint, cx, 44, TextAlign.Center, 1, 1f);
                s.Text("HOLD OVER CRATE", cx, 62, TextAlign.Center, 1, 0.45f);
            }
            s.End(ready ? "MagnetReady" : "MagnetRange");
            return true;
        }

        // ---------------------------------------------------------------- monitor

        public bool DrawMonitor(CockpitScreen s)
        {
            if (tappableMonitor != s)
            {
                if (tappableMonitor != null)
                    tappableMonitor.Tapped -= OnMonitorTapped;
                tappableMonitor = s;
                s.Tapped += OnMonitorTapped;
            }

            // The station redraws on its own interval, so measure the time between draws
            float dt = Mathf.Min(Time.time - lastDraw, 0.25f);
            lastDraw = Time.time;
            switch (mode)
            {
                case MonitorMode.Scanning:
                    modeTime += dt;
                    if (modeTime >= ScanTime)
                    {
                        revealed?.Invoke();
                        revealed = null;
                        BeginReading();
                        break;
                    }
                    DrawScan(s);
                    return true;

                case MonitorMode.Reading:
                    pageTime += dt;
                    if (pageTime >= PageTime)
                        NextPage();
                    break;
            }
            if (mode != MonitorMode.Reading)
                return false;
            DrawPage(s);
            return true;
        }

        void OnMonitorTapped(CockpitScreen _)
        {
            if (mode == MonitorMode.Reading)
                NextPage();
        }

        void BeginReading()
        {
            pages.Clear();
            var lines = PixelCanvas.Wrap(content.text, BodyChars * PixelFont.Advance - 1);
            for (int i = 0; i < lines.Count; i += BodyLines)
                pages.Add(lines.GetRange(i, Mathf.Min(BodyLines, lines.Count - i)).ToArray());
            page = 0;
            pageTime = 0f;
            pagesShown = 1;
            mode = MonitorMode.Reading;
        }

        /// <summary>Turns the page; after showing everything twice the monitor goes back to its status page.</summary>
        void NextPage()
        {
            pageTime = 0f;
            pagesShown++;
            if (pagesShown > pages.Count * 2)
            {
                mode = MonitorMode.Idle;
                return;
            }
            page = (page + 1) % pages.Count;
        }

        void DrawScan(CockpitScreen s)
        {
            int cx = s.Width / 2;
            float t = modeTime / ScanTime;
            s.Begin();
            s.SetColor(null);
            s.Text("SCANNING CRATE", cx, 5, TextAlign.Center);
            s.Rule(16);
            s.Rect(8, 24, s.Width - 16, 9, 0.7f);
            s.FillRect(10, 26, Mathf.RoundToInt((s.Width - 20) * t), 5, 0.95f);
            // Readouts flicker while the scan runs
            float r = Mathf.Floor(Time.time * 8f);
            s.Text($"DENS {0.6f + Mathf.Repeat(r * 0.37f, 0.4f):0.00}", 8, 40, TextAlign.Left, 1, 0.6f);
            s.Text($"MAG  {Mathf.Repeat(r * 13.7f, 99f):00.0}", 8, 50, TextAlign.Left, 1, 0.6f);
            if (Blink(3f))
                s.Text("...", s.Width - 8, 50, TextAlign.Right, 1, 0.8f);
            s.End("Scan");
        }

        void DrawPage(CockpitScreen s)
        {
            int cx = s.Width / 2;
            s.Begin();
            s.SetColor(content.kind switch
            {
                CrateContentKind.Crystal => CrystalColor,
                CrateContentKind.Artifact => ArtifactColor,
                _ => null,
            });

            bool alert = content.kind == CrateContentKind.Crystal && page == 0 && pagesShown <= pages.Count;
            if (!alert || Blink(2f, 0.7f))
                s.Text(header, 4, 4, TextAlign.Left, 1, 1f);
            if (pages.Count > 1)
                s.Text($"{page + 1}/{pages.Count}", s.Width - 4, 4, TextAlign.Right, 1, 0.5f);
            s.Rule(14);
            s.Text(content.title, cx, 18, TextAlign.Center, 1, 0.9f);
            var lines = pages[page];
            for (int i = 0; i < lines.Length; i++)
                s.Text(lines[i], cx, 30 + i * PixelFont.LineHeight, TextAlign.Center, 1, 0.75f);
            s.End($"Page{header}{page}");
        }

        static bool Blink(float rate, float duty = 0.5f) => Mathf.Repeat(Time.time * rate, 1f) < duty;
    }
}
