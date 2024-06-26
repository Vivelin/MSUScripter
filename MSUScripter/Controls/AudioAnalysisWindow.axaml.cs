﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MSUScripter.Services;
using MSUScripter.ViewModels;

namespace MSUScripter.Controls;

public partial class AudioAnalysisWindow : ScalableWindow
{
    private readonly AudioAnalysisService? _audioAnalysisService;
    private MsuProjectViewModel? _project;
    private readonly AudioAnalysisViewModel _rows;
    private readonly CancellationTokenSource _cts = new();

    public AudioAnalysisWindow() : this(null)
    {
    }
    
    public AudioAnalysisWindow(AudioAnalysisService? audioAnalysisService)
    {
        _audioAnalysisService = audioAnalysisService;
        InitializeComponent();
        DataContext = _rows = new AudioAnalysisViewModel(); 
    }

    public void SetProject(MsuProjectViewModel project)
    {
        if (_audioAnalysisService == null) return;

        _project = project;

        var msuDirectory = new FileInfo(project.MsuPath).DirectoryName;
        if (string.IsNullOrEmpty(msuDirectory)) return;

        var songs = project.Tracks.SelectMany(x => x.Songs)
            .OrderBy(x => x.TrackNumber)
            .Select(x => new AudioAnalysisSongViewModel()
            {
                SongName = Path.GetRelativePath(msuDirectory, new FileInfo(x.OutputPath!).FullName),
                TrackName = x.TrackName,
                TrackNumber = x.TrackNumber,
                Path = x.OutputPath ?? "",
                OriginalViewModel = x,
            })
            .ToList();

        _rows.Rows = songs;
        _rows.TotalSongs = songs.Count;
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_audioAnalysisService == null || _project == null) return;

        _ = Task.Run(async () =>
        {
            var start = DateTime.Now;
            
            await _audioAnalysisService!.AnalyzePcmFiles(_project!, _rows, _cts.Token);

            var avg = GetAverageRms();
            var max = GetAveragePeak();

            if (_cts.Token.IsCancellationRequested) return;

            foreach (var row in _rows.Rows)
            {
                CheckSongWarnings(row, avg, max);
            }
            
            UpdateBottomMessage();

            var end = DateTime.Now;
            var span = end - start;
            
            Dispatcher.UIThread.Invoke(() =>
            {
                Title = $"Audio Analysis - MSU Scripter (Completed in {Math.Round(span.TotalSeconds, 2)} seconds)";
            });
        }, _cts.Token);
    }

    private double GetAverageRms() => Math.Round(_rows.Rows.Where(x => x.AvgDecibals != null).Average(x => x.AvgDecibals) ?? 0, 4);
    private double GetAveragePeak() => Math.Round(_rows.Rows.Where(x => x.MaxDecibals != null).Average(x => x.MaxDecibals) ?? 0, 4);

    private void RefreshSongButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_audioAnalysisService == null || _project == null) return;
        if (sender is not Button button) return;
        if (button.Tag is not AudioAnalysisSongViewModel song) return;
        
        song.AvgDecibals = null;
        song.MaxDecibals = null;
        song.HasWarning = false;
        song.HasLoaded = false;

        _ = Task.Run(async () =>
        {
            await _audioAnalysisService!.AnalyzePcmFile(_project!, song);
            CheckSongWarnings(song, GetAverageRms(), GetAveragePeak());
            UpdateBottomMessage();
        });
    }

    private void CheckSongWarnings(AudioAnalysisSongViewModel song, double averageVolume, double maxVolume)
    {
        if (song.AvgDecibals != null && Math.Abs(song.AvgDecibals.Value - averageVolume) > 4)
        {
            song.HasWarning = true;
            song.WarningMessage =
                $"This song's average volume of {song.AvgDecibals} differs greatly from the average volume of all songs, {averageVolume}";
        }
        else if (song.MaxDecibals != null && song.MaxDecibals - maxVolume > 4)
        {
            song.HasWarning = true;
            song.WarningMessage =
                $"This song's peak volume of {song.MaxDecibals} differs greatly from the average peak volume of all songs, {maxVolume}";
        }
    }

    private void Control_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
    }

    private void UpdateBottomMessage()
    {
        _rows.BottomBar = $"{GetAverageRms()} Total Average Decibals | {GetAveragePeak()} Average Peak Decibals";
    }
}