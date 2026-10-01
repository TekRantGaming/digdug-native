// Front end - the "ROM required" page and loading a ROM set (drag and drop, or a native file picker).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DigDug
{
    public sealed unsafe partial class App
    {
        volatile string pendingPath;
        string romError;
        int noRomFrames, animTick;
        volatile bool browsing;
        readonly int[] frameBuf = new int[W * H];

        void RenderRomRequired()
        {
            animTick++;
            int bg = unchecked((int)0xff101830), gold = unchecked((int)0xffffd800), white = unchecked((int)0xffe8e8ff);
            int green = unchecked((int)0xff80ff80), red = unchecked((int)0xffff6060), dim = unchecked((int)0xff8890b0);
            for (int i = 0; i < frameBuf.Length; i++) frameBuf[i] = bg;
            Action<string, int, int, int> center = (s, y, col, scale) => Font5x7.Draw(frameBuf, W, H, s, (W - Font5x7.Width(s, scale)) / 2, y, col, scale);
            center("DIG DUG", 14, gold, 4);
            center("NATIVE PORT", 50, white, 1);
            center("ROM FILES REQUIRED", 74, red, 1);
            center("THE GAME ROMS ARE NOT", 88, dim, 1);
            center("INCLUDED - BRING YOUR OWN", 98, dim, 1);

            // marching-ants drop box
            int bx0 = 14, by0 = 122, bx1 = W - 15, by1 = 206;
            for (int x = bx0; x <= bx1; x++)
                for (int k = 0; k < 2; k++)
                {
                    int y = k == 0 ? by0 : by1;
                    if (((x + animTick / 3) / 4) % 2 == 0) { frameBuf[y * W + x] = gold; frameBuf[(y + 1) * W + x] = gold; }
                }
            for (int y = by0; y <= by1; y++)
                for (int k = 0; k < 2; k++)
                {
                    int x = k == 0 ? bx0 : bx1;
                    if (((y + animTick / 3) / 4) % 2 == 0) { frameBuf[y * W + x] = gold; frameBuf[y * W + x + 1] = gold; }
                }
            center("DRAG AND DROP", 142, white, 2);
            center("YOUR DIGDUG.ZIP", 164, gold, 2);
            center("ONTO THIS WINDOW", 186, white, 1);

            center(browsing ? "CHOOSE THE FILE IN THE DIALOG..." : "OR PRESS ENTER / CLICK / A", 220, green, 1);
            center("TO BROWSE FOR IT", 232, green, 1);
            if (romError != null) center(romError, 252, red, 1);
            else center("SEE README FOR DETAILS", 264, dim, 1);
            Present(frameBuf);
        }

        // Native file picker (PowerShell/WinForms on Windows, zenity or kdialog on Linux) on a background thread,
        // so the window keeps running - and keeps accepting drag-and-drop - while the dialog is open.
        void BrowseForRom()
        {
            if (browsing) return;
            browsing = true;
            var th = new System.Threading.Thread(() =>
            {
                try { string p = PickFile(); if (!string.IsNullOrEmpty(p)) pendingPath = p; }
                finally { browsing = false; }
            }) { IsBackground = true, Name = "file picker" };
            th.Start();
        }

        string PickFile()
        {
            string[][] attempts;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                attempts = new[] { new[] { "powershell", "-NoProfile", "-STA", "-Command",
                    "Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.OpenFileDialog; $d.Title = 'Select your Dig Dug ROM zip'; $d.Filter = 'ROM set (*.zip)|*.zip|All files (*.*)|*.*'; if ($d.ShowDialog() -eq 'OK') { $d.FileName }" } };
            else
                attempts = new[] { new[] { "zenity", "--file-selection", "--title=Select your Dig Dug ROM zip" }, new[] { "kdialog", "--getopenfilename", ".", "*.zip" } };
            foreach (var a in attempts)
            {
                try
                {
                    var psi = new ProcessStartInfo(a[0]) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    for (int i = 1; i < a.Length; i++) psi.ArgumentList.Add(a[i]);
                    using (var proc = Process.Start(psi))
                    {
                        string outp = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit();
                        return outp.Length > 0 ? outp.Split('\n')[0].Trim() : null;
                    }
                }
                catch (Exception ex) { Log("file dialog '" + a[0] + "' unavailable: " + ex.Message); }
            }
            romError = "NO FILE DIALOG - USE DRAG AND DROP";
            return null;
        }

        void OnDrop(string path)
        {
            Log("drop/open: " + path);
            if (machine != null || string.IsNullOrEmpty(path)) return;
            // a loose ROM file (not a .zip) dropped from an extracted set: use its folder
            if (File.Exists(path) && !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) path = Path.GetDirectoryName(path);
            try
            {
                var roms = RomSet.Load(path);
                cfg.RomPath = path; cfg.Save();
                Program.RomSource = path;
                romError = null;
                StartMachine(roms, null);
            }
            catch (Exception ex)
            {
                Log("ROM load failed: " + ex);
                string m = ex.Message;
                romError = m.StartsWith("Missing ROM files") ? "ROM SET INCOMPLETE - TRY AGAIN" : "NOT A DIG DUG ROM SET";
            }
        }
    }
}
