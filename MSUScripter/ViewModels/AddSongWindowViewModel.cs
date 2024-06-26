using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MSUScripter.ViewModels;

public class AddSongWindowViewModel : INotifyPropertyChanged
{
    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            SetField(ref _filePath, value);
            OnPropertyChanged(nameof(CanEditMainFields));
            OnPropertyChanged(nameof(CanAddSong));
        }
    }

    private List<string> _tracks = new();
    public List<string> Tracks
    {
        get => _tracks;
        set
        {
            SetField(ref _tracks, value);
            OnPropertyChanged(nameof(CanEditMainFields));
        }
    }
    
    private int _selectedIndex;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            SetField(ref _selectedIndex, value);
            OnPropertyChanged(nameof(CanAddSong));
        }
    }
    
    private string? _selectedTrackName;
    public string? SelectedTrackName
    {
        get => _selectedTrackName;
        set
        {
            SetField(ref _selectedTrackName, value);
        }
    }

    private string? _songName;
    public string? SongName
    {
        get => _songName;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _songName, value);
        }
    }

    private bool _displayHertzWarning;

    public bool DisplayHertzWarning
    {
        get => _displayHertzWarning;
        set => SetField(ref _displayHertzWarning, value);
    }
    
    private string? _artistName;
    public string? ArtistName
    {
        get => _artistName;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _artistName, value);
        }
    }
    
    private string? _albumName;
    public string? AlbumName
    {
        get => _albumName;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _albumName, value);
        }
    }
    
    private int? _trimStart;
    public int? TrimStart
    {
        get => _trimStart;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _trimStart, value);
        }
    }
    
    private int? _trimEnd;
    public int? TrimEnd
    {
        get => _trimEnd;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _trimEnd, value);
        }
    }
    
    private int? _loopPoint;
    public int? LoopPoint
    {
        get => _loopPoint;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _loopPoint, value);
        }
    }
    
    private double? _normalization;
    public double? Normalization
    {
        get => _normalization;
        set
        {
            if (value != null)
            {
                HasModified = true;
            }
            SetField(ref _normalization, value);
        }
        
    }

    private string? _averageAudio; 
    public string? AverageAudio
    {
        get => _averageAudio;
        set => SetField(ref _averageAudio, value);
        
    }
    
    private string? _peakAudio; 
    public string? PeakAudio
    {
        get => _peakAudio;
        set
        {
            SetField(ref _peakAudio, value);
            OnPropertyChanged(nameof(HasAudioAnalysis));
        }
    }

    private bool _enableSearchBox;

    public bool EnableSearchBox
    {
        get => _enableSearchBox;
        set => SetField(ref _enableSearchBox, value);
    }

    private string _addSongButtonText = "Add Song";

    public string AddSongButtonText
    {
        get => _addSongButtonText;
        set => SetField(ref _addSongButtonText, value);
    }

    private string _trackDescription = "";

    public string TrackDescription
    {
        get => _trackDescription;
        set
        {
            HasDescription = !string.IsNullOrEmpty(value);
            SetField(ref _trackDescription, value);   
        }
    }

    private bool _hasDescription;
    public bool HasDescription
    {
        get => _hasDescription;
        set => SetField(ref _hasDescription, value);
    }

    public void Clear()
    {
        FilePath = "";
        SongName = "";
        ArtistName = "";
        AlbumName = "";
        TrimStart = null;
        TrimEnd = null;
        LoopPoint = null;
        Normalization = null;
        AverageAudio = null;
        PeakAudio = null;
        HasModified = false;
    }

    public bool HasAudioAnalysis => !string.IsNullOrEmpty(_peakAudio);

    public bool CanEditMainFields => !string.IsNullOrEmpty(FilePath);

    public bool CanAddSong => !string.IsNullOrEmpty(FilePath) && SelectedIndex > 0 && !RunningPyMusicLooper;
    
    private bool _hasModified; 
    public bool HasModified
    {
        get => _hasModified;
        set => SetField(ref _hasModified, value);
    }
    
    private bool _runningPyMusicLooper; 
    public bool RunningPyMusicLooper
    {
        get => _runningPyMusicLooper;
        set
        {
            SetField(ref _runningPyMusicLooper, value);
            OnPropertyChanged(nameof(CanAddSong));
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        
        
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}