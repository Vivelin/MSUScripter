using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MSUScripter.Configs;
using MSUScripter.Services;
using MSUScripter.ViewModels;

namespace MSUScripter.Controls;

public partial class SelectTrackWindow : Window
{

    private SelectTrackWindowViewModel _model = new();
    
    public SelectTrackWindow()
    {
        InitializeComponent();
        DataContext = _model;
    }

    public void ShowDialog(Window window, MsuProjectViewModel msuProjectViewModel, MsuTrackInfoViewModel trackViewModel,
        MsuSongInfoViewModel msuSongInfoViewModel, bool isMove)
    {
        _model.Project = msuProjectViewModel;
        _model.PreviousTrack = trackViewModel;
        _model.PreviousSong = msuSongInfoViewModel;
        _model.IsMove = isMove;
        _model.TrackNames = msuProjectViewModel.Tracks
            .OrderBy(x => x.TrackNumber)
            .Select(x => $"Track #{x.TrackNumber} - {x.TrackName}")
            .ToList();
        _model.Tracks = msuProjectViewModel.Tracks.OrderBy(x => x.TrackNumber).ToList();
        _model.SelectedIndex = _model.Tracks.IndexOf(_model.PreviousTrack);
        this.Find<ComboBox>(nameof(TrackComboBox))!.SelectedIndex = _model.SelectedIndex;
        ShowDialog(window);
    }

    private void OkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_model.PreviousTrack == null || _model.PreviousSong == null || _model.Project == null)
        {
            return;
        }
        
        var destinationTrack = _model.Tracks[_model.SelectedIndex];
        if (_model.IsMove)
        {
            if (destinationTrack.TrackNumber == _model.PreviousTrack.TrackNumber)
            {
                return;
            }

            var songInfo = _model.PreviousSong;

            songInfo.TrackNumber = destinationTrack.TrackNumber;
            songInfo.TrackName = destinationTrack.TrackName;
            songInfo.IsAlt = destinationTrack.Songs.Count > 0;
            songInfo.MsuPcmInfo.IsAlt = songInfo.IsAlt;

            var msu = new FileInfo(_model.Project.MsuPath);
            if (!_model.PreviousSong.IsAlt)
            {
                songInfo.OutputPath = msu.FullName.Replace(msu.Extension, $"-{destinationTrack.TrackNumber}.pcm");
            }
            else
            {
                var altSuffix = destinationTrack.Songs.Count == 1 ? "alt" : $"alt{destinationTrack.Songs.Count}";
                songInfo.OutputPath =
                    msu.FullName.Replace(msu.Extension, $"-{destinationTrack.TrackNumber}_{altSuffix}.pcm");
            }

            _model.PreviousTrack.Songs.Remove(_model.PreviousSong);
            destinationTrack.Songs.Add(_model.PreviousSong);
        }
        else
        {
            var msuSongInfo = new MsuSongInfo(); 
            ConverterService.Instance.ConvertViewModel(_model.PreviousSong, msuSongInfo);
            
            msuSongInfo.TrackNumber = destinationTrack.TrackNumber;
            msuSongInfo.TrackName = destinationTrack.TrackName;
            msuSongInfo.IsAlt = destinationTrack.Songs.Count > 0;

            var msu = new FileInfo(_model.Project.MsuPath);
            if (!_model.PreviousSong.IsAlt)
            {
                msuSongInfo.OutputPath = msu.FullName.Replace(msu.Extension, $"-{destinationTrack.TrackNumber}.pcm");
            }
            else
            {
                var altSuffix = destinationTrack.Songs.Count == 1 ? "alt" : $"alt{destinationTrack.Songs.Count}";
                msuSongInfo.OutputPath =
                    msu.FullName.Replace(msu.Extension, $"-{destinationTrack.TrackNumber}_{altSuffix}.pcm");
            }
            
            var msuSongInfoCloned = new MsuSongInfo(); 
            ConverterService.Instance.ConvertViewModel(msuSongInfo, msuSongInfoCloned);
            destinationTrack.Songs.Add(_model.PreviousSong);
        }
        
        Close();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}