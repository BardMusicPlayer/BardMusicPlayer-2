using BardMusicPlayer.Coffer;
using BardMusicPlayer.Functions;
using BardMusicPlayer.Pigeonhole;
using BardMusicPlayer.Resources;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BardMusicPlayer.Controls;

/// <summary>
/// Web scraper to scrape the song list from https://songs.bardmusicplayer.com and populate in the listview
/// </summary>
public partial class MidiRepository
{
    private List<Song> _fullListSong         = new();
    private List<Song> _previewListSong      = new();
    
    private Song? _selectedSong;
    private bool _isDownloading;

    public MidiRepository()
    {
        XIVMIDI.XIVMIDI.Instance.OnRequestFinished += Instance_RequestFinished;
        InitializeComponent();
        LoadingProgressBar.Visibility    = Visibility.Hidden;
        DownloadPanel.Visibility         = Visibility.Hidden;
        DownloadPath.Text                = BmpPigeonhole.Instance.MidiDownloadPath;
        DownloadProgressLabel.Visibility = Visibility.Hidden;
        DownloadProgressBar.Visibility   = Visibility.Hidden;
        PerformerSize_box.ItemsSource    = XIVMIDI.IO.Misc.PerformerSize.Values;
        RefreshPlaylistSelector();
        BmpCoffer.Instance.OnPlaylistDataUpdated += RefreshPlaylistSelector;
    }

    private class Song
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Arranger { get; set; } = "";
        public string Url { get; set; } = "";
    }

    private void PerformerSize_box_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    /// <summary>
    /// Send a request to API
    /// </summary>
    private void SendRequest()
    {
        this.Dispatcher.BeginInvoke(new Action(() =>
        {
            List<string> filteredList = new List<string>();
            filteredList.Add("Loading...");
            MidiRepoContainer.ItemsSource = filteredList;
        }));

        string url = new XIVMIDI.IO.BMPAPIRequestBuilder() { bandSize = PerformerSize_box.SelectedIndex }.BuildRequest();
        XIVMIDI.XIVMIDI.Instance.AddToQueue(new XIVMIDI.IO.GetRequest()
        {
            Url = url,
            Host = new Uri(url).Host,
            RequestSource = 1,
            Requester = XIVMIDI.IO.Requester.JSON
        });
    }

    /// <summary>
    /// Downloads a song
    /// </summary>
    /// <param name="filename"></param>
    private void DownloadSong(string filename)
    {
        XIVMIDI.XIVMIDI.Instance.AddToQueue(new XIVMIDI.IO.GetRequest()
        {
            Url = Uri.EscapeUriString(filename),
            Host = new Uri(filename).Host,
            Accept = "audio/midi",
            Requester = XIVMIDI.IO.Requester.DOWNLOAD
        });
    }

    /// <summary>
    /// Finished request data
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Instance_RequestFinished(object sender, object e)
    {
        if (e == null)
            return;

        if (e is XIVMIDI.IO.GetRequest)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                List<string> filteredList = new List<string>();
                filteredList.Add("Service not available!");
                MidiRepoContainer.ItemsSource = filteredList;
            }));
        }
        if (e is XIVMIDI.IO.BMPResponseContainer.Root)
        {
            _fullListSong.Clear();
            _previewListSong.Clear();

            var data = e as XIVMIDI.IO.BMPResponseContainer.Root;
            if (data == null)
            {
                List<string> filteredList = new List<string>();
                filteredList.Add("No data loaded!");
                MidiRepoContainer.ItemsSource = filteredList;
                return;
            }
            foreach (var file in data.docs)
            {
                try
                {
                    if (file.url.Length <= 2)
                        continue;
                    _fullListSong.Add(new Song
                    {
                        Title = file.title ?? "",
                        Artist = file.artist ?? "",
                        Arranger = file.arranger ?? "",
                        Url = file.url,
                    });
                }
                catch { }
            }
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                _previewListSong = _fullListSong;
                MidiRepoContainer.ItemsSource = _previewListSong.Select(song => song.Title).ToList();
                RefreshCountTextBox();

                BtnGetSongList.IsEnabled = true;
                BtnGetSongList.Content = "Refresh";
                LoadingProgressBar.Visibility = Visibility.Hidden;
                SongSearchTextBox.Text = "";
            }));
        }
        else if (e is XIVMIDI.IO.XIVMIDIResponseContainer.MidiFile)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                var data = e as XIVMIDI.IO.XIVMIDIResponseContainer.MidiFile;
                if (data == null)
                    return;
                var downloadsPath = BmpPigeonhole.Instance.MidiDownloadPath;
                var finalFilePath = $"{downloadsPath}/{data.Filename}.mid";

                if (File.Exists(finalFilePath))
                    File.Delete(finalFilePath);             // Delete the existing file

                using (var tempFileStream = new FileStream(finalFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    tempFileStream.WriteAsync(data.data, 0, data.data.Length);
                    Dispatcher.Invoke(() => DownloadProgressBar.Value = 100);
                }

                DownloadPanel.IsEnabled = true;
                DownloadProgressLabel.Visibility = Visibility.Visible;
                _isDownloading = false;

                // Add to selected playlist
                var addToPlaylist = AddToPlaylistCheckBox.IsChecked ?? false;

                if (addToPlaylist && PlaylistDropdown.SelectedIndex != -1)
                    AddSongToPlaylist(finalFilePath);

            }));
        }
    }

    /// <summary>
    /// Click button to scrape the web and put the result to listSong
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Button_Click(object sender, RoutedEventArgs e)
    {
        BtnGetSongList.IsEnabled      = false;
        LoadingProgressBar.Visibility = Visibility.Visible;
        SendRequest();
    }

    /// <summary>
    /// Show midi details when clicking the listview
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MidiRepoContainer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MidiRepoContainer.SelectedIndex == -1)
            return;

        DownloadPanel.Visibility = Visibility.Visible;
        _selectedSong            = _previewListSong[MidiRepoContainer.SelectedIndex];
        SongTitle.Text           = $"({_selectedSong.Artist}) {_selectedSong.Title}";
        SongComment.Text         = _selectedSong.Arranger;
    }

    /// <summary>
    /// Select download path on click button
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SelectPath_Button_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FolderPicker
        {
            InputPath = Directory.Exists(BmpPigeonhole.Instance.MidiDownloadPath) ? Path.GetFullPath(BmpPigeonhole.Instance.MidiDownloadPath) : Path.GetDirectoryName(AppContext.BaseDirectory)
        };

        if (dlg.ShowDialog() == true)
        {
            var path = dlg.ResultPath;
            if (!Directory.Exists(path))
                return;

            path                                    += path != null && path.EndsWith("\\") ? "" : "\\";
            DownloadPath.Text                       =  path;
            BmpPigeonhole.Instance.MidiDownloadPath =  path;
        }
    }

    /// <summary>
    /// Download selected midi in the listview by clicking download button
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void DownloadButtonClick(object sender, RoutedEventArgs e)
    {
        DownloadSelectedMidi();
    }

    /// <summary>
    /// Download selected midi by double click the list item
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MidiRepoContainer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _selectedSong = _previewListSong[MidiRepoContainer.SelectedIndex];
        DownloadSelectedMidi();
    }

    /// <summary>
    /// Download current selected midi in the listview
    /// </summary>
    private void DownloadSelectedMidi()
    {
        if (_isDownloading)
            return;

        if (!Directory.Exists(BmpPigeonhole.Instance.MidiDownloadPath))
        {
            MessageBox.Show("The downloads directory is not valid.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_selectedSong == null)
            return;

        DownloadPanel.IsEnabled        = false;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.Value      = 0;
        _isDownloading = true;
        DownloadSong(_selectedSong.Url);
    }
   
    /// <summary>
    /// Refresh result count textblock
    /// </summary>
    private void RefreshCountTextBox()
    {
        ResultsCountTextBox.Text = $"{_previewListSong.Count} Results";
    }

    #region Search Functions
    /// <summary>
    /// Filter the midi listview based on SongSearchTextBox
    /// </summary>
    private void SearchSong()
    {
        if (_fullListSong.Count == 0)
            return;

        List<string> filteredList;
        if (SongSearchTextBox.Text != "")
        {
            _previewListSong = _fullListSong.FindAll(s => s.Title.ToLower().Contains(SongSearchTextBox.Text.ToLower()));
            filteredList     = _previewListSong.Select(s => s.Title).ToList();
        }
        else
        {
            _previewListSong = _fullListSong;
            filteredList     = _previewListSong.Select(s => s.Title).ToList();
        }

        MidiRepoContainer.ItemsSource = filteredList;
        RefreshCountTextBox();
    }
    /// <summary>
    /// Filter song when textbox value changed
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SongSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchSong();
    }
    #endregion

    #region Import To Playlist Functions
    /// <summary>
    /// Refresh playlist dropdown
    /// </summary>
    private void RefreshPlaylistSelector()
    {
        PlaylistDropdown.DataContext = BmpCoffer.Instance.GetPlaylistNames();
    }

    /// <summary>
    /// Disable 'add to playlist' feature if checkbox is unchecked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void AddToPlaylistCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        RefreshAddToPlaylistMode();
    }

    /// <summary>
    /// Enable 'add to playlist' feature if checkbox is checked
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void AddToPlaylistCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        RefreshAddToPlaylistMode();
    }

    /// <summary>
    /// Hide and show playlist selector while check/uncheck 'add to playlist' checkbox
    /// </summary>
    private void RefreshAddToPlaylistMode()
    {
        var isChecked = AddToPlaylistCheckBox.IsChecked ?? false;
        PlaylistDropdown.Visibility = isChecked ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>
    /// Add song to playlist by song filepath
    /// </summary>
    private void AddSongToPlaylist(string filePath)
    {
        var playlist = BmpCoffer.Instance.GetPlaylist(PlaylistDropdown.SelectedItem as string);
        PlaylistFunctions.AddFileToPlaylist(filePath, playlist);
    }
    #endregion
}