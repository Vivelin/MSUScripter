﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using MSUScripter.Configs;
using MSUScripter.Services;
using MSUScripter.UI.Tools;
using MSUScripter.ViewModels;

namespace MSUScripter.UI;

public partial class MsuSongMsuPcmInfoPanel
{
    private readonly bool _isSubTrack;
    private readonly bool _isSubChannel;
    private readonly MsuSongMsuPcmInfoPanel? _parent;
    private MsuSongInfoPanel? _parentSongPanel;

    public MsuSongMsuPcmInfoPanel() : this(false, false, null)
    {
    }

    public MsuSongMsuPcmInfoPanel(bool isSubTrack, bool isSubChannel, MsuSongMsuPcmInfoPanel? parent)
    {
        _isSubTrack = isSubTrack;
        _isSubChannel = isSubChannel;
        _parent = parent;
        InitializeComponent();
        DataContext = MsuSongMsuPcmInfo = new MsuSongMsuPcmInfoViewModel();
        if (isSubTrack)
        {
            SubTrackLabeledControl.Visibility = Visibility.Collapsed;
            HeaderText.Text = "Sub Track Details";
        }
        else if (isSubChannel)
        {
            SubChannelLabeledControl.Visibility = Visibility.Collapsed;
            HeaderText.Text = "Sub Channel Details";
        }
        else
        {
            RemoveButton.Visibility = Visibility.Collapsed;
        }
    }
    
    public MsuSongMsuPcmInfoViewModel MsuSongMsuPcmInfo { get; set; }

    public List<MsuSongMsuPcmInfoPanel> SubTrackPanels { get; } = new();
    public List<MsuSongMsuPcmInfoPanel> SubChannelPanels { get; } = new();

    public void ApplyMsuSongMsuPcmInfo(MsuSongMsuPcmInfo data)
    {
        ConverterService.ConvertViewModel(data, MsuSongMsuPcmInfo, false);
        MsuSongMsuPcmInfo.LastModifiedDate = data.LastModifiedDate; 

        if (!_isSubChannel && data.SubChannels.Any())
        {
            foreach (var subChannel in data.SubChannels)
            {
                AddSubChannel(subChannel);
            }
        }
        
        if (!_isSubTrack && data.SubTracks.Any())
        {
            foreach (var subTrack in data.SubTracks)
            {
                AddSubTrack(subTrack);
            }
        }
    }

    public void AddSubChannel(MsuSongMsuPcmInfo? subChannel)
    {
        subChannel ??= new MsuSongMsuPcmInfo();
        var newPanel = new MsuSongMsuPcmInfoPanel(false, true, this);
        newPanel.ApplyMsuSongMsuPcmInfo(subChannel);
        SubChannelStackPanel.Children.Add(newPanel);
        SubChannelPanels.Add(newPanel);
        AddSubTrackButton.IsEnabled = false;
    }
    
    public void AddSubTrack(MsuSongMsuPcmInfo? subTrack)
    {
        subTrack ??= new MsuSongMsuPcmInfo();
        var newPanel = new MsuSongMsuPcmInfoPanel(true, false, this);
        newPanel.ApplyMsuSongMsuPcmInfo(subTrack);
        SubTrackStackPanel.Children.Add(newPanel);
        SubTrackPanels.Add(newPanel);
        AddSubChannelButton.IsEnabled = false;
    }

    public void RemoveSubChannel(MsuSongMsuPcmInfoPanel subChannel)
    {
        SubChannelStackPanel.Children.Remove(subChannel);
        SubChannelPanels.Remove(subChannel);
        if (!_isSubChannel && !_isSubTrack)
        {
            if (!SubChannelPanels.Any())
            {
                AddSubTrackButton.IsEnabled = true;
            }
        }
    }
    
    public void RemoveSubTrack(MsuSongMsuPcmInfoPanel subChannel)
    {
        SubTrackStackPanel.Children.Remove(subChannel);
        SubTrackPanels.Remove(subChannel);
        if (!_isSubChannel && !_isSubTrack)
        {
            if (!SubTrackPanels.Any())
            {
                AddSubChannelButton.IsEnabled = true;
            }
        }
    }

    public void ShowMsuPcmButtons(MsuSongInfoPanel parentSongPanel)
    {
        _parentSongPanel = parentSongPanel;
        MsuPcmButtonsStackPanel.Visibility = Visibility.Visible;
    }

    public MsuSongMsuPcmInfo GetData()
    {
        var data = new MsuSongMsuPcmInfo();
        ConverterService.ConvertViewModel(MsuSongMsuPcmInfo, data, false);
        
        if (!_isSubChannel)
        {
            data.SubChannels = SubChannelPanels.Select(x => x.GetData()).ToList();
        }
        
        if (!_isSubTrack)
        {
            data.SubTracks = SubTrackPanels.Select(x => x.GetData()).ToList();
        }

        return data;
    }
    
    public bool HasChangesSince(DateTime time)
    {
        if (MsuSongMsuPcmInfo.LastModifiedDate > time)
        {
            return true;
        }

        foreach (var subChannel in SubChannelPanels)
        {
            if (subChannel.HasChangesSince(time))
                return true;
        }
        
        foreach (var subTrack in SubTrackPanels)
        {
            if (subTrack.HasChangesSince(time))
                return true;
        }

        return false;
    }

    public async Task PlaySong(bool fromEnd)
    {
        if (_parentSongPanel?.MsuSongInfo.OutputPath == null)
            return;
        
        // Stop the song if it is currently playing
        if (_parentSongPanel.IsMsuPcmProject && HasChangesSince(_parentSongPanel.LastPcmGenerationTime) && HasAnyFiles())
        {
            await StopSong();
            if (!_parentSongPanel!.GeneratePcmFile(false))
                return;
        }
        
        EditPanel.Instance?.UpdateStatusBarText("Playing Song");
        await AudioService.Instance.PlaySongAsync(_parentSongPanel.MsuSongInfo.OutputPath, fromEnd);
    }

    public bool HasAnyFiles()
    {
        if (!string.IsNullOrEmpty(MsuSongMsuPcmInfo.File))
            return true;

        if (SubChannelPanels.Any(x => x.HasAnyFiles()))
            return true;
        
        if (SubTrackPanels.Any(x => x.HasAnyFiles()))
            return true;

        return false;
    }

    public async Task StopSong()
    {
        EditPanel.Instance?.UpdateStatusBarText("Stopping Song");
        await AudioService.Instance.StopSongAsync(_parentSongPanel?.MsuSongInfo.OutputPath);
        EditPanel.Instance?.UpdateStatusBarText("Stopped Song");
    }
    
    private void DecimalTextBox_OnPreviewTextInput(object s, TextCompositionEventArgs e) =>
        Helpers.DecimalTextBox_OnPreviewTextInput(s, e);

    private void DecimalTextBox_OnPaste(object s, DataObjectPastingEventArgs e) =>
        Helpers.DecimalTextBox_OnPaste(s, e);

    private void DecimalTextBox_OnLostFocus(object s, RoutedEventArgs e) =>
        Helpers.DecimalTextBox_OnLostFocus(s, e);
    
    private void IntTextBox_OnPreviewTextInput(object s, TextCompositionEventArgs e) =>
        Helpers.IntTextBox_OnPreviewTextInput(s, e);

    private void IntTextBox_OnPaste(object s, DataObjectPastingEventArgs e) =>
        Helpers.IntTextBox_OnPaste(s, e);

    private void IntTextBox_OnLostFocus(object s, RoutedEventArgs e) =>
        Helpers.IntTextBox_OnLostFocus(s, e);

    private void AddSubChannelButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddSubChannel(null);
    }

    private void AddSubTrackButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddSubTrack(null);
    }

    private void RemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSubChannel && _parent != null)
        {
            var result = MessageBox.Show("Are you sure you want to remove this Sub Channel?", "Warning", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _parent.RemoveSubChannel(this);
        }
        else if (_isSubTrack && _parent != null)
        {
            var result = MessageBox.Show("Are you sure you want to remove this Sub Track?", "Warning", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
                _parent.RemoveSubTrack(this);
        }
    }

    private void FileButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new CommonOpenFileDialog()
        {
            EnsurePathExists = true,
            Title = "Select Music File",
        };
        
        if (dialog.ShowDialog() != CommonFileDialogResult.Ok || string.IsNullOrEmpty(dialog.FileName) || !File.Exists(dialog.FileName)) return;

        MsuSongMsuPcmInfo.File = dialog.FileName;
        
        ImportAudioMetadata(dialog.FileName);
    }

    private void GeneratePcmFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_parentSongPanel == null) return;
        Task.Run(async () =>
        {
            await StopSong();
            return _parentSongPanel.GeneratePcmFile(false);
        });
    }

    private void GenerateAsMainPcmFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_parentSongPanel == null) return;
        Task.Run(async () =>
        {
            await StopSong();
            return _parentSongPanel.GeneratePcmFile(true);
        });
    }

    private void PlaySongButton_OnClick(object sender, RoutedEventArgs e)
    {
        Task.Run(() => PlaySong(false));
    }

    private void TestLoopButton_OnClick(object sender, RoutedEventArgs e)
    {
        Task.Run(() => PlaySong(true));
    }

    private void ImportAudioMetadata(string file)
    {
        if (_parent != null)
        {
            _parent.ImportAudioMetadata(file);
        }
        else if (_parentSongPanel != null)
        {
            _parentSongPanel.ImportAudioMetadata(file);
        }
    }

    private void ClearFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        MsuSongMsuPcmInfo.File = null;
    }
}