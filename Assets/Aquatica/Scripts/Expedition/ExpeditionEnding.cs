using System.Collections;
using Aquatica.Cockpit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Aquatica.Expedition
{
    /// <summary>
    /// The end of the demo, once the submarine passes through the gateway: the view floods with light, then a CRT
    /// card sums up the dive (logs and artifacts recovered, time taken). Tapping starts again from the real world.
    /// </summary>
    public class ExpeditionEnding : MonoBehaviour
    {
        CrtOverlay overlay;
        int logs, artifacts;
        float seconds;
        bool canRestart;

        public static ExpeditionEnding Play(Shader crtShader, int logs, int artifacts, float seconds)
        {
            var ending = new GameObject("ExpeditionEnding").AddComponent<ExpeditionEnding>();
            ending.logs = logs;
            ending.artifacts = artifacts;
            ending.seconds = seconds;
            ending.overlay = CrtOverlay.Create(crtShader, 140);
            ending.overlay.transform.SetParent(ending.transform, false);
            ending.overlay.SetFeed(null);
            ending.StartCoroutine(ending.Run());
            return ending;
        }

        IEnumerator Run()
        {
            // Light floods in
            var white = new Color(0.85f, 1f, 1f);
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                overlay.SetFade(Mathf.SmoothStep(0f, 1f, t / 2f), white);
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);

            // ...and the signal is lost
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                float k = t / 1.2f;
                overlay.SetFade(1f - k, white);
                overlay.SetStatic(Mathf.Sin(k * Mathf.PI) * 0.9f);
                yield return null;
            }
            overlay.SetFade(0f, white);
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
            string[] text =
            {
                "TRANSMISSION ENDS",
                "",
                "DSRV AQUATICA",
                "PASSED THE GATE",
                "",
                $"LOGS      {logs}/{ExpeditionStory.TotalLogs}",
                $"ARTIFACTS {artifacts}/{ExpeditionStory.Artifacts.Length}",
                $"DIVE TIME {(int)(seconds / 60f):00}:{(int)(seconds % 60f):00}",
            };
            // Centred block: the lines, a gap, the prompt
            int y = (s.Height - (text.Length * LineGap + 12 + PixelFont.GlyphHeight)) / 2;
            for (int i = 0; i < text.Length && i < lines; i++)
                s.Text(text[i], cx, y + i * LineGap, TextAlign.Center, 1, i == 0 ? 1f : 0.8f);
            if (canRestart && Mathf.Repeat(Time.time, 1.2f) < 0.8f)
                s.Text("TAP TO DIVE AGAIN", cx, y + text.Length * LineGap + 12, TextAlign.Center, 1, 0.6f);
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
