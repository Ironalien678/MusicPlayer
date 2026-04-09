using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MusicPlayer
{
    public partial class Form1 : Form
    {
        private dynamic wmp;
        private List<string> playlist = new List<string>();
        private int currentIndex = -1;
        private bool isSeeking = false;
        private bool shuffleEnabled = false;
        private bool repeatEnabled = false;
        private bool isMuted = false;
        private int savedVolume = 50;
        private List<string> fullPlaylist = new List<string>();
        private Random rng = new Random();
        private static readonly string SaveDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicPlayer");
        private static readonly string SaveFile = System.IO.Path.Combine(SaveDir, "session.dat");

        public Form1()
        {
            InitializeComponent();

            // create Windows Media Player COM object via ProgID so no project reference is required
            try
            {
                var t = Type.GetTypeFromProgID("WMPlayer.OCX");
                if (t != null)
                {
                    wmp = Activator.CreateInstance(t);
                    try { wmp.settings.volume = 50; } catch { }
                }
            }
            catch
            {
                wmp = null;
            }

            // restore previous session
            LoadSession();

            // check for updates silently on startup
            System.Threading.Tasks.Task.Run(() =>
            {
                try { UpdateChecker.Check(silent: true); } catch { }
            });
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            UpdateChecker.Check(silent: false);
        }

        private void lstPlaylist_DoubleClick(object sender, EventArgs e)
        {
            if (lstPlaylist.SelectedIndex >= 0) PlayAt(lstPlaylist.SelectedIndex);
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() != DialogResult.OK) return;
            bool wasEmpty = playlist.Count == 0;
            foreach (var f in openFileDialog1.FileNames)
            {
                playlist.Add(f);
                lstPlaylist.Items.Add(System.IO.Path.GetFileName(f));
            }
            // autoplay: start playing the first added track if nothing was playing
            if (wasEmpty && playlist.Count > 0 && currentIndex < 0)
                PlayAt(0);
        }

        private void addFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select a folder containing music files";
                fbd.ShowNewFolderButton = false;
                if (fbd.ShowDialog() != DialogResult.OK) return;

                bool wasEmpty = playlist.Count == 0;
                var audioExt = new[] { ".mp3", ".wav", ".wma", ".flac" };
                try
                {
                    var files = System.IO.Directory.GetFiles(fbd.SelectedPath, "*.*", SearchOption.AllDirectories)
                        .Where(x => audioExt.Contains(System.IO.Path.GetExtension(x).ToLowerInvariant()))
                        .OrderBy(x => x)
                        .ToArray();

                    foreach (var f in files)
                    {
                        playlist.Add(f);
                        lstPlaylist.Items.Add(System.IO.Path.GetFileName(f));
                    }

                    UpdateStatusBar();

                    // autoplay if nothing was playing
                    if (wasEmpty && playlist.Count > 0 && currentIndex < 0)
                        PlayAt(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error scanning folder: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void PlayAt(int index)
        {
            if (index < 0 || index >= playlist.Count) return;
            currentIndex = index;
            var file = playlist[index];
            lblNowPlaying.Text = "Now Playing: " + System.IO.Path.GetFileName(file);
            try
            {
                if (wmp != null) wmp.URL = file;
            }
            catch { }
            try { if (wmp != null) wmp.controls.play(); } catch { }
            lstPlaylist.SelectedIndex = index;
            timer1.Start();
            // update album art when playing
            try { UpdateAlbumArt(file); } catch { }
            // update metadata display
            try { UpdateMetadataDisplay(file); } catch { }
            UpdateStatusBar();
        }

        private void UpdateMetadataDisplay(string path)
        {
            try
            {
                lblTitle.Text = "Title: ";
                lblArtist.Text = "Artist: ";
                lblAlbum.Text = "Album: ";
                // Try ID3 tags for MP3 using simple parsing (limited)
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".mp3")
                {
                    var tags = ReadId3Tags(path);
                    if (tags.HasValue)
                    {
                        var t = tags.Value;
                        if (!string.IsNullOrWhiteSpace(t.Title)) lblTitle.Text = "Title: " + t.Title;
                        if (!string.IsNullOrWhiteSpace(t.Artist)) lblArtist.Text = "Artist: " + t.Artist;
                        if (!string.IsNullOrWhiteSpace(t.Album)) lblAlbum.Text = "Album: " + t.Album;
                        return;
                    }
                }
                // fallback: use filename for title
                lblTitle.Text = "Title: " + System.IO.Path.GetFileNameWithoutExtension(path);
            }
            catch { }
        }

        private (string Title, string Artist, string Album)? ReadId3Tags(string mp3Path)
        {
            try
            {
                string title = null, artist = null, album = null;
                using (var fs = new System.IO.FileStream(mp3Path, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (var br = new System.IO.BinaryReader(fs))
                {
                    var header = br.ReadBytes(10);
                    if (header.Length < 10) return null;
                    if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return null;
                    int size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
                    int start = 10;
                    while (start < 10 + size)
                    {
                        fs.Position = start;
                        var frameIdBytes = br.ReadBytes(4);
                        if (frameIdBytes.Length < 4) break;
                        var frameId = Encoding.ASCII.GetString(frameIdBytes);
                        var frameSizeBytes = br.ReadBytes(4);
                        if (frameSizeBytes.Length < 4) break;
                        int frameSize = (frameSizeBytes[0] << 24) | (frameSizeBytes[1] << 16) | (frameSizeBytes[2] << 8) | frameSizeBytes[3];
                        var frameFlags = br.ReadBytes(2);
                        var frameData = br.ReadBytes(frameSize);
                        if (frameSize <= 0) { start += 10 + frameSize; continue; }
                        if (frameId == "TIT2" || frameId == "TPE1" || frameId == "TALB")
                        {
                            int idx = 0;
                            var encoding = frameData[0];
                            idx = 1;
                            string value = string.Empty;
                            try
                            {
                                if (encoding == 0)
                                {
                                    value = Encoding.GetEncoding("ISO-8859-1").GetString(frameData, idx, frameData.Length - idx).TrimEnd((char)0);
                                }
                                else if (encoding == 1)
                                {
                                    value = Encoding.Unicode.GetString(frameData, idx, frameData.Length - idx).TrimEnd((char)0);
                                }
                                else if (encoding == 3)
                                {
                                    value = Encoding.UTF8.GetString(frameData, idx, frameData.Length - idx).TrimEnd((char)0);
                                }
                                else
                                {
                                    value = Encoding.UTF8.GetString(frameData, idx, frameData.Length - idx).TrimEnd((char)0);
                                }
                            }
                            catch { }
                            if (frameId == "TIT2" && string.IsNullOrWhiteSpace(title)) title = value;
                            if (frameId == "TPE1" && string.IsNullOrWhiteSpace(artist)) artist = value;
                            if (frameId == "TALB" && string.IsNullOrWhiteSpace(album)) album = value;
                        }
                        start += 10 + frameSize;
                        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album)) break;
                    }
                }
                if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(album))
                    return (title, artist, album);
            }
            catch { }
            return null;
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            if (lstPlaylist.SelectedIndex >= 0) PlayAt(lstPlaylist.SelectedIndex);
            else if (currentIndex >= 0) try { wmp.controls.play(); timer1.Start(); } catch { }
            else if (playlist.Count > 0) PlayAt(0);
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            try
            {
                if (wmp == null) return;
                var state = 0;
                try { state = (int)wmp.playState; } catch { }
                // 3 = Playing, 2 = Paused
                if (state == 3)
                {
                    try { wmp.controls.pause(); btnPause.Text = "Resume"; } catch { }
                }
                else
                {
                    try { wmp.controls.play(); btnPause.Text = "⏸"; } catch { }
                }
            }
            catch { }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            try { if (wmp != null) wmp.controls.stop(); } catch { }
            timer1.Stop();
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            PlayNext();
        }

        private void PlayNext()
        {
            if (playlist.Count == 0) return;
            if (repeatEnabled)
            {
                PlayAt(currentIndex);
                return;
            }
            if (shuffleEnabled)
            {
                int next = rng.Next(playlist.Count);
                PlayAt(next);
                return;
            }
            int nextIdx = currentIndex + 1;
            if (nextIdx >= playlist.Count) nextIdx = 0;
            PlayAt(nextIdx);
        }

        private void btnPrev_Click(object sender, EventArgs e)
        {
            if (playlist.Count == 0) return;
            int prev = currentIndex - 1;
            if (prev < 0) prev = playlist.Count - 1;
            PlayAt(prev);
        }

        private void trackVolume_Scroll(object sender, EventArgs e)
        {
            try { if (wmp != null) wmp.settings.volume = trackVolume.Value; } catch { }
            lblVolume.Text = trackVolume.Value + "%";
            if (trackVolume.Value == 0) btnMute.Text = "🔇";
            else { btnMute.Text = "🔊"; isMuted = false; }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                if (wmp == null) return;
                int state = 0;
                try { state = (int)wmp.playState; } catch { }
                // 8 = MediaEnded
                if (state == 8) { PlayNext(); return; }

                var media = wmp.currentMedia;
                if (media != null)
                {
                    double dur = 0;
                    try { dur = (double)media.duration; } catch { }
                    double pos = 0;
                    try { pos = (double)wmp.controls.currentPosition; } catch { }
                    if (dur > 0)
                    {
                        if (!isSeeking)
                        {
                            try { trackPosition.Maximum = Math.Max(1, (int)Math.Ceiling(dur)); } catch { }
                            try { trackPosition.Value = Math.Min(trackPosition.Maximum, (int)Math.Round(pos)); } catch { }
                        }
                        lblPosition.Text = SecondsToTimeString((int)Math.Round(pos));
                        lblDuration.Text = SecondsToTimeString((int)Math.Round(dur));
                        // update progress fill bar
                        try
                        {
                            int fillWidth = (int)(panelSeekBg.Width * (pos / dur));
                            panelSeekFill.Width = Math.Max(0, Math.Min(panelSeekBg.Width, fillWidth));
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static string SecondsToTimeString(int secs)
        {
            try
            {
                var t = TimeSpan.FromSeconds(secs);
                if (t.Hours > 0) return string.Format("{0}:{1:D2}:{2:D2}", (int)t.TotalHours, t.Minutes, t.Seconds);
                return string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
            }
            catch { return "00:00"; }
        }

        private void trackPosition_Scroll(object sender, EventArgs e)
        {
            try { lblPosition.Text = SecondsToTimeString(trackPosition.Value); } catch { }
        }

        private void trackPosition_MouseDown(object sender, MouseEventArgs e)
        {
            isSeeking = true;
        }

        private void trackPosition_MouseUp(object sender, MouseEventArgs e)
        {
            isSeeking = false;
            try { if (wmp != null) wmp.controls.currentPosition = trackPosition.Value; } catch { }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "Playlist|*.m3u;*.txt|All files|*.*";
                sfd.DefaultExt = "m3u";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try { System.IO.File.WriteAllLines(sfd.FileName, playlist); }
                    catch (Exception ex) { MessageBox.Show("Failed to save playlist: " + ex.Message); }
                }
            }
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Playlist|*.m3u;*.txt|All files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(ofd.FileName).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                        playlist.Clear(); lstPlaylist.Items.Clear();
                        foreach (var l in lines) { playlist.Add(l); lstPlaylist.Items.Add(System.IO.Path.GetFileName(l)); }
                        if (playlist.Count > 0) PlayAt(0);
                    }
                    catch (Exception ex) { MessageBox.Show("Failed to load playlist: " + ex.Message); }
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Music Player\nBuilt with Windows Media Player COM object.\n", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Drag & drop support for playlist
        private void lstPlaylist_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        private void lstPlaylist_DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                bool wasEmpty = playlist.Count == 0;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files)
                {
                    if (System.IO.Directory.Exists(f))
                    {
                        // add audio files from directory recursively
                        var audioExt = new[] { ".mp3", ".wav", ".wma", ".flac" };
                        var found = System.IO.Directory.GetFiles(f, "*.*", SearchOption.AllDirectories)
                            .Where(x => audioExt.Contains(System.IO.Path.GetExtension(x).ToLowerInvariant()));
                        foreach (var x in found)
                        {
                            playlist.Add(x);
                            lstPlaylist.Items.Add(System.IO.Path.GetFileName(x));
                        }
                    }
                    else if (System.IO.File.Exists(f))
                    {
                        var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                        if (new[] { ".mp3", ".wav", ".wma", ".flac" }.Contains(ext))
                        {
                            playlist.Add(f);
                            lstPlaylist.Items.Add(System.IO.Path.GetFileName(f));
                        }
                    }
                }
                // autoplay: start playing the first added track if nothing was playing
                if (wasEmpty && playlist.Count > 0 && currentIndex < 0)
                    PlayAt(0);
            }
            catch { }
        }

        // Try to use TagLib via reflection if it's available at runtime (optional dependency)
        private void TryTagLibUpdate(string path)
        {
            try
            {
                // Look for TagLib.File type in loaded assemblies or by name
                Type fileType = Type.GetType("TagLib.File, TagLib") ?? Type.GetType("TagLib.File, taglib-sharp")
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("TagLib.File")).FirstOrDefault(t => t != null);
                if (fileType == null) return; // TagLib not present

                var createMethod = fileType.GetMethod("Create", new Type[] { typeof(string) });
                if (createMethod == null) return;
                var fileObj = createMethod.Invoke(null, new object[] { path });
                if (fileObj == null) return;

                var tagProp = fileType.GetProperty("Tag");
                var tagObj = tagProp?.GetValue(fileObj);
                if (tagObj != null)
                {
                    var tagType = tagObj.GetType();
                    var titleProp = tagType.GetProperty("Title");
                    var performersProp = tagType.GetProperty("Performers");
                    var albumProp = tagType.GetProperty("Album");
                    try { var title = titleProp?.GetValue(tagObj) as string; if (!string.IsNullOrWhiteSpace(title)) lblTitle.Text = "Title: " + title; } catch { }
                    try { var performers = performersProp?.GetValue(tagObj) as string[]; if (performers != null && performers.Length > 0) lblArtist.Text = "Artist: " + string.Join(", ", performers); } catch { }
                    try { var album = albumProp?.GetValue(tagObj) as string; if (!string.IsNullOrWhiteSpace(album)) lblAlbum.Text = "Album: " + album; } catch { }

                    // pictures
                    try
                    {
                        var picturesProp = tagType.GetProperty("Pictures");
                        var pics = picturesProp?.GetValue(tagObj) as System.Array;
                        if (pics != null && pics.Length > 0)
                        {
                            var picObj = pics.GetValue(0);
                            var dataProp = picObj.GetType().GetProperty("Data");
                            var dataObj = dataProp?.GetValue(picObj);
                            if (dataObj != null)
                            {
                                // Try Data as byte[] or a ByteVector with ToArray()
                                byte[] bytes = null;
                                if (dataObj is byte[] b) bytes = b;
                                else
                                {
                                    var toArray = dataObj.GetType().GetMethod("ToArray");
                                    if (toArray != null) bytes = toArray.Invoke(dataObj, null) as byte[];
                                    else
                                    {
                                        var innerData = dataObj.GetType().GetProperty("Data")?.GetValue(dataObj) as byte[];
                                        if (innerData != null) bytes = innerData;
                                    }
                                }
                                if (bytes != null)
                                {
                                    using (var ms = new MemoryStream(bytes))
                                    {
                                        picAlbumArt.Image = Image.FromStream(ms);
                                    }
                                    return;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Album art extraction: try to read embedded cover art from common formats (MP3 ID3)
        private void UpdateAlbumArt(string path)
        {
            try
            {
                picAlbumArt.Image = null;
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".mp3")
                {
                    // read ID3 tag cover using simple binary scan (no external libs)
                    var tag = ReadId3Cover(path);
                    if (tag != null)
                    {
                        using (var ms = new System.IO.MemoryStream(tag))
                        {
                            var img = Image.FromStream(ms);
                            picAlbumArt.Image = new Bitmap(img);
                        }
                        return;
                    }
                }

                // As a fallback, try to look for folder.jpg or cover.jpg next to file
                var dir = System.IO.Path.GetDirectoryName(path);
                foreach (var name in new[] { "folder.jpg", "cover.jpg", "album.jpg" })
                {
                    var p = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(p))
                    {
                        picAlbumArt.Image = Image.FromFile(p);
                        return;
                    }
                }
            }
            catch { picAlbumArt.Image = null; }
        }

        // Very small ID3v2 APIC extractor: returns raw image bytes or null
        private byte[] ReadId3Cover(string mp3Path)
        {
            try
            {
                using (var fs = new System.IO.FileStream(mp3Path, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                using (var br = new System.IO.BinaryReader(fs))
                {
                    var header = br.ReadBytes(10);
                    if (header.Length < 10) return null;
                    if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return null;
                    int size = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
                    var frameHeader = new List<byte>();
                    var bytesRead = 0;
                    while (bytesRead < size - 10)
                    {
                        var frameIdBytes = br.ReadBytes(4);
                        if (frameIdBytes.Length < 4) break;
                        var frameId = Encoding.ASCII.GetString(frameIdBytes);
                        var frameSizeBytes = br.ReadBytes(4);
                        if (frameSizeBytes.Length < 4) break;
                        int frameSize = (frameSizeBytes[0] << 24) | (frameSizeBytes[1] << 16) | (frameSizeBytes[2] << 8) | frameSizeBytes[3];
                        var frameFlags = br.ReadBytes(2);
                        bytesRead += 10 + frameSize;
                        if (frameSize <= 0) continue;
                        var frameData = br.ReadBytes(frameSize);
                        if (frameId == "APIC")
                        {
                            // APIC: text encoding (1) + mime (null-terminated) + picture type + description (null-term) + binary data
                            int index = 0;
                            // skip text encoding
                            index++;
                            // read mime until 0x00
                            while (index < frameData.Length && frameData[index] != 0) index++;
                            index++;
                            if (index >= frameData.Length) continue;
                            // skip picture type
                            index++;
                            // skip description until 0x00
                            while (index < frameData.Length && frameData[index] != 0) index++;
                            index++;
                            if (index >= frameData.Length) continue;
                            var imgData = new byte[frameData.Length - index];
                            Array.Copy(frameData, index, imgData, 0, imgData.Length);
                            return imgData;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // --- Shuffle ---
        private void btnShuffle_Click(object sender, EventArgs e)
        {
            shuffleEnabled = !shuffleEnabled;
            btnShuffle.Text = shuffleEnabled ? "Shuffle ✓" : "Shuffle";
            UpdateStatusBar();
        }

        // --- Repeat ---
        private void btnRepeat_Click(object sender, EventArgs e)
        {
            repeatEnabled = !repeatEnabled;
            btnRepeat.Text = repeatEnabled ? "Repeat ✓" : "Repeat";
            UpdateStatusBar();
        }

        // --- Mute ---
        private void btnMute_Click(object sender, EventArgs e)
        {
            try
            {
                if (wmp == null) return;
                if (!isMuted)
                {
                    savedVolume = trackVolume.Value;
                    trackVolume.Value = 0;
                    try { wmp.settings.volume = 0; } catch { }
                    btnMute.Text = "🔇";
                    lblVolume.Text = "0%";
                    isMuted = true;
                }
                else
                {
                    trackVolume.Value = savedVolume;
                    try { wmp.settings.volume = savedVolume; } catch { }
                    btnMute.Text = "🔊";
                    lblVolume.Text = savedVolume + "%";
                    isMuted = false;
                }
            }
            catch { }
        }

        // --- Search / filter playlist ---
        private void txtSearch_Enter(object sender, EventArgs e)
        {
            if (txtSearch.ForeColor == Color.Gray)
            {
                txtSearch.Text = "";
                txtSearch.ForeColor = SystemColors.WindowText;
            }
        }

        private void txtSearch_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                txtSearch.ForeColor = Color.Gray;
                txtSearch.Text = "Search playlist...";
            }
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            if (txtSearch.ForeColor == Color.Gray) return;
            var query = txtSearch.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(query))
            {
                // restore full playlist
                lstPlaylist.Items.Clear();
                foreach (var p in playlist) lstPlaylist.Items.Add(System.IO.Path.GetFileName(p));
                return;
            }
            lstPlaylist.Items.Clear();
            for (int i = 0; i < playlist.Count; i++)
            {
                var name = System.IO.Path.GetFileName(playlist[i]).ToLowerInvariant();
                if (name.Contains(query)) lstPlaylist.Items.Add(System.IO.Path.GetFileName(playlist[i]));
            }
        }

        // --- Context menu: Remove track ---
        private void removeTrackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (lstPlaylist.SelectedIndex < 0) return;
            int idx = lstPlaylist.SelectedIndex;
            // find actual playlist index by matching name
            var selectedName = lstPlaylist.Items[idx].ToString();
            int realIdx = playlist.FindIndex(p => System.IO.Path.GetFileName(p) == selectedName);
            if (realIdx >= 0)
            {
                playlist.RemoveAt(realIdx);
                if (currentIndex == realIdx) { currentIndex = -1; }
                else if (currentIndex > realIdx) { currentIndex--; }
            }
            lstPlaylist.Items.RemoveAt(idx);
            UpdateStatusBar();
        }

        // --- Context menu: Clear playlist ---
        private void clearPlaylistToolStripMenuItem_Click(object sender, EventArgs e)
        {
            playlist.Clear();
            lstPlaylist.Items.Clear();
            currentIndex = -1;
            lblNowPlaying.Text = "Now Playing: ";
            lblTitle.Text = "Title: ";
            lblArtist.Text = "Artist: ";
            lblAlbum.Text = "Album: ";
            picAlbumArt.Image = null;
            try { if (wmp != null) wmp.controls.stop(); } catch { }
            timer1.Stop();
            UpdateStatusBar();
        }

        // --- Context menu: Move Up ---
        private void moveUpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int idx = lstPlaylist.SelectedIndex;
            if (idx <= 0) return;
            // swap in playlist
            var temp = playlist[idx]; playlist[idx] = playlist[idx - 1]; playlist[idx - 1] = temp;
            // swap in listbox
            var item = lstPlaylist.Items[idx];
            lstPlaylist.Items.RemoveAt(idx);
            lstPlaylist.Items.Insert(idx - 1, item);
            lstPlaylist.SelectedIndex = idx - 1;
            // adjust currentIndex
            if (currentIndex == idx) currentIndex--;
            else if (currentIndex == idx - 1) currentIndex++;
        }

        // --- Context menu: Move Down ---
        private void moveDownToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int idx = lstPlaylist.SelectedIndex;
            if (idx < 0 || idx >= lstPlaylist.Items.Count - 1) return;
            // swap in playlist
            var temp = playlist[idx]; playlist[idx] = playlist[idx + 1]; playlist[idx + 1] = temp;
            // swap in listbox
            var item = lstPlaylist.Items[idx];
            lstPlaylist.Items.RemoveAt(idx);
            lstPlaylist.Items.Insert(idx + 1, item);
            lstPlaylist.SelectedIndex = idx + 1;
            // adjust currentIndex
            if (currentIndex == idx) currentIndex++;
            else if (currentIndex == idx + 1) currentIndex--;
        }

        // --- Keyboard shortcuts ---
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // don't intercept if search box is focused
            if (txtSearch.Focused) return base.ProcessCmdKey(ref msg, keyData);

            switch (keyData)
            {
                case Keys.Space:
                    btnPause_Click(this, EventArgs.Empty);
                    return true;
                case Keys.MediaNextTrack:
                    PlayNext();
                    return true;
                case Keys.MediaPreviousTrack:
                    btnPrev_Click(this, EventArgs.Empty);
                    return true;
                case Keys.Right:
                    // seek forward 5s
                    try { if (wmp != null) wmp.controls.currentPosition += 5; } catch { }
                    return true;
                case Keys.Left:
                    // seek back 5s
                    try { if (wmp != null) wmp.controls.currentPosition -= 5; } catch { }
                    return true;
                case Keys.Up:
                    if (trackVolume.Value < 100) { trackVolume.Value = Math.Min(100, trackVolume.Value + 5); trackVolume_Scroll(this, EventArgs.Empty); }
                    return true;
                case Keys.Down:
                    if (trackVolume.Value > 0) { trackVolume.Value = Math.Max(0, trackVolume.Value - 5); trackVolume_Scroll(this, EventArgs.Empty); }
                    return true;
                case Keys.Delete:
                    removeTrackToolStripMenuItem_Click(this, EventArgs.Empty);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // --- Status bar update ---
        private void UpdateStatusBar()
        {
            var status = "";
            if (currentIndex >= 0 && playlist.Count > 0)
                status = "Track " + (currentIndex + 1) + " of " + playlist.Count;
            else
                status = playlist.Count + " tracks";
            if (shuffleEnabled) status += "  |  Shuffle ON";
            if (repeatEnabled) status += "  |  Repeat ON";
            toolStripStatusLabel1.Text = status;
        }

        private void toolStripStatusLabel1_Click(object sender, EventArgs e)
        {

        }

        private void SaveSession()
        {
            try
            {
                if (!System.IO.Directory.Exists(SaveDir))
                    System.IO.Directory.CreateDirectory(SaveDir);

                var lines = new List<string>();
                // settings line
                lines.Add(string.Format("##SETTINGS##|{0}|{1}|{2}|{3}",
                    trackVolume.Value,
                    shuffleEnabled ? "1" : "0",
                    repeatEnabled ? "1" : "0",
                    currentIndex));
                // playlist paths
                foreach (var p in playlist)
                    lines.Add(p);

                System.IO.File.WriteAllLines(SaveFile, lines);
            }
            catch { }
        }

        private void LoadSession()
        {
            try
            {
                if (!System.IO.File.Exists(SaveFile)) return;

                var lines = System.IO.File.ReadAllLines(SaveFile);
                int restoredIndex = -1;

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("##SETTINGS##|"))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 5)
                        {
                            int vol;
                            if (int.TryParse(parts[1], out vol))
                            {
                                trackVolume.Value = Math.Max(0, Math.Min(100, vol));
                                try { if (wmp != null) wmp.settings.volume = trackVolume.Value; } catch { }
                                lblVolume.Text = trackVolume.Value + "%";
                            }
                            if (parts[2] == "1") { shuffleEnabled = true; btnShuffle.Text = "Shuffle ✓"; }
                            if (parts[3] == "1") { repeatEnabled = true; btnRepeat.Text = "Repeat ✓"; }
                            int idx;
                            if (int.TryParse(parts[4], out idx)) restoredIndex = idx;
                        }
                        continue;
                    }

                    // it's a playlist path
                    if (System.IO.File.Exists(line))
                    {
                        playlist.Add(line);
                        lstPlaylist.Items.Add(System.IO.Path.GetFileName(line));
                    }
                }

                UpdateStatusBar();

                // restore selection (don't auto-play, just highlight)
                if (restoredIndex >= 0 && restoredIndex < playlist.Count)
                {
                    currentIndex = restoredIndex;
                    lstPlaylist.SelectedIndex = restoredIndex;
                    var file = playlist[restoredIndex];
                    lblNowPlaying.Text = "Now Playing: " + System.IO.Path.GetFileName(file);
                    try { UpdateAlbumArt(file); } catch { }
                    try { UpdateMetadataDisplay(file); } catch { }
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSession();
            try { if (wmp != null) wmp.controls.stop(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
