using System.Collections;
using Aquatica.Cockpit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The end of a dive, as a CRT card summing it up. Through the gateway (<see cref="Play"/>): the view floods with
    /// light, then the logs and artifacts recovered and the time taken. Drowned (<see cref="PlayFailure"/>): murky
    /// water closes over the view, the signal breaks up, then what was recovered before the hull flooded. Either way,
    /// tapping starts again from the real world (portal placement).
    /// </summary>
    public class ExpeditionEnding : MonoBehaviour
    {
        CrtOverlay overlay;
        string[] cardLines;
        string prompt;
        Color fadeColor;
        float fadeIn, hold;
        bool canRestart;

        public static ExpeditionEnding Play(Shader crtShader, int logs, int artifacts, float seconds) =>
            Create(crtShader, new Color(0.85f, 1f, 1f), 2f, 0.6f, "TAP TO DIVE AGAIN",
                "TRANSMISSION ENDS",
                "",
                "AQUATICA",
                "PASSED THE GATE",
                "",
                $"LOGS      {logs}/{ExpeditionStory.TotalLogs}",
                $"ARTIFACTS {artifacts}/{ExpeditionStory.Artifacts.Length}",
                $"DIVE TIME {Clock(seconds)}");

        /// <summary>The cabin flooded and the player drowned.</summary>
        public static ExpeditionEnding PlayFailure(Shader crtShader, int crates, int totalCrates, int crystals, float seconds) =>
            Create(crtShader, new Color(0.02f, 0.13f, 0.15f), 1.4f, 1.2f, "TAP TO TRY AGAIN",
                "SIGNAL LOST",
                "",
                "AQUATICA",
                "HULL FLOODED",
                "",
                $"CRATES    {crates}/{totalCrates}",
                $"CRYSTALS  {crystals}/{ExpeditionStory.CrystalCount}",
                $"DIVE TIME {Clock(seconds)}");

        static ExpeditionEnding Create(Shader crtShader, Color fadeColor, float fadeIn, float hold, string prompt, params string[] lines)
        {
            var ending = new GameObject("ExpeditionEnding").AddComponent<ExpeditionEnding>();
            ending.cardLines = lines;
            ending.prompt = prompt;
            ending.fadeColor = fadeColor;
            ending.fadeIn = fadeIn;
            ending.hold = hold;
            ending.overlay = CrtOverlay.Create(crtShader, 140);
            ending.overlay.transform.SetParent(ending.transform, false);
            ending.overlay.SetFeed(null);
            ending.StartCoroutine(ending.Run());
            return ending;
        }

        static string Clock(float seconds) => $"{(int)(seconds / 60f):00}:{(int)(seconds % 60f):00}";

        IEnumerator Run()
        {
            // Light (or water) floods in
            for (float t = 0f; t < fadeIn; t += Time.deltaTime)
            {
                overlay.SetFade(Mathf.SmoothStep(0f, 1f, t / fadeIn), fadeColor);
                yield return null;
            }
            yield return new WaitForSeconds(hold);

            // ...and the signal is lost
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float k = t / 1.2f;
                overlay.SetFade(1f - k, fadeColor);
                overlay.SetStatic(Mathf.Sin(k * Mathf.PI) * 0.9f);
                yield return null;
            }
            overlay.SetFade(0f, fadeColor);
            overlay.SetStatic(0f);

            float shown = 0f;
            while (true)
            {
                shown += Time.deltaTime;
                Draw(shown);
                canRestart = shown > 3f;
                if (canRestart && Tapped())
                    break;
                yield return null;
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Draw(float shown)
        {
            var s = overlay.Hud;
            s.Clear();
            const int LineGap = 10;
            int cx = s.Width / 2;
            // Lines type in one after another
            int lines = Mathf.FloorToInt(shown / 0.5f);
            var text = cardLines;
            // Centred block: the lines, a gap, the prompt
            int y = (s.Height - (text.Length * LineGap + 12 + PixelFont.GlyphHeight)) / 2;
            for (int i = 0; i < text.Length && i < lines; i++)
                s.Text(text[i], cx, y + i * LineGap, TextAlign.Center, 1, i == 0 ? 1f : 0.8f);
            if (canRestart && Mathf.Repeat(Time.time, 1.2f) < 0.8f)
                s.Text(prompt, cx, y + text.Length * LineGap + 12, TextAlign.Center, 1, 0.6f);
            overlay.Present();
        }

        static bool Tapped()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                return true;
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }
    }
}
