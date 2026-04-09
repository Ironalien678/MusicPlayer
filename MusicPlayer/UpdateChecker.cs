using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Windows.Forms;

namespace MusicPlayer
{
    internal static class UpdateChecker
    {
        // ---------------------------------------------------------------
        // HOW TO USE:
        // 1. Host a plain text file at the URL below (e.g., GitHub raw, your website, etc.)
        // 2. The file should have exactly 2 lines:
        //      Line 1: latest version number, e.g. "1.1.0.0"
        //      Line 2: download URL for the new installer
        //    Example contents of version.txt:
        //      1.1.0.0
        //      https://github.com/Ironalien678/MusicPlayer/releases/tag/v1.2
        // 3. When you release a new version, update AssemblyVersion in
        //    Properties\AssemblyInfo.cs AND update the hosted version.txt.
        // ---------------------------------------------------------------

        private const string VersionUrl = "https://raw.githubusercontent.com/Ironalien678/MusicPlayer/master/version.txt";

        /// <summary>
        /// Check for updates. If silent is true, only show a message when an update is available.
        /// </summary>
        public static void Check(bool silent = false)
        {
            try
            {
                // GitHub requires TLS 1.2
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string response;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "MusicPlayer-UpdateChecker");
                    response = wc.DownloadString(VersionUrl);
                }

                if (string.IsNullOrWhiteSpace(response))
                {
                    if (!silent) MessageBox.Show("Could not check for updates.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length < 2)
                {
                    if (!silent) MessageBox.Show("Update file format is invalid.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var latestVersionStr = lines[0].Trim();
                var downloadUrl = lines[1].Trim();

                Version latestVersion;
                if (!Version.TryParse(latestVersionStr, out latestVersion))
                {
                    if (!silent) MessageBox.Show("Could not parse latest version.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                if (latestVersion > currentVersion)
                {
                    var result = MessageBox.Show(
                        "A new version (" + latestVersion + ") is available!\n" +
                        "You are running version " + currentVersion + ".\n\n" +
                        "Would you like to download the update?",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        Process.Start(downloadUrl);
                    }
                }
                else
                {
                    if (!silent)
                        MessageBox.Show("You are running the latest version (" + currentVersion + ").",
                            "No Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Could not check for updates:\n" + ex.Message,
                        "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
