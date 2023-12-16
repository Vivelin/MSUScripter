using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MSUScripter.Configs;

namespace MSUScripter.Services;

public class AudioPlayerServiceLinux : IAudioPlayerService
{
    private readonly ILogger<AudioPlayerServiceLinux> _logger;
    private readonly Settings _settings;
    private readonly PythonCommandRunnerService _python;
    private Process? _process;
    private bool _isPlaying;
    private bool _isTestingLoop;
    
    public AudioPlayerServiceLinux(ILogger<AudioPlayerServiceLinux> logger, Settings settings, PythonCommandRunnerService python)
    {
        _logger = logger;
        _settings = settings;
        _python = python;
        if (_python.SetBaseCommand("pcm_player", "--version", out var result, out var error) &&
            result.StartsWith("pcm_player "))
        {
            CanPlayMusic = true;
            IAudioPlayerService.CanPlaySongs = true;
        }
    }

    public string CurrentPlayingFile { get; set; } = "";
    
    public void Pause()
    {
        if (_process?.HasExited == false)
        {
            _isPlaying = false;
            _process.Exited -= ProcessOnExited;
            _process.Kill();
            _process = null;
            PlayStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PlayPause()
    {
        if (_isPlaying)
        {
            Pause();
        }
        else if (!string.IsNullOrEmpty(CurrentPlayingFile))
        {
            PlaySongAsync(CurrentPlayingFile, _isTestingLoop);
        }
    }

    public void Play()
    {
        // Do nothing
        return;
    }

    public double? GetCurrentPosition()
    {
        return null;
    }

    public double GetLengthSeconds()
    {
        // Do nothing
        return 0;
    }

    public double GetCurrentPositionSeconds()
    {
        // Do nothing
        return 0;
    }

    public void SetPosition(double value)
    {
        // Do nothing
    }

    public void SetVolume(double volume)
    {
        // Do nothing
    }

    public Task<bool> PlaySongAsync(string path, bool fromEnd)
    {
        Pause();
        _isTestingLoop = fromEnd;
        CurrentPlayingFile = path;
        if (fromEnd)
        {
            _process = _python.RunCommandAsync($"-f \"{path}\" -l");
        }
        else
        {
            _process = _python.RunCommandAsync($"-f \"{path}\"");
        }

        if (_process != null)
        {
            _isPlaying = true;
            _process.Exited += ProcessOnExited;
            _process.Disposed += ProcessOnExited;
            PlayStarted?.Invoke(this, EventArgs.Empty);

            Task.Run(() =>
            {
                var processToWaitFor = _process;
                processToWaitFor.WaitForExit();
                if (_process == processToWaitFor)
                {
                    Pause();
                }
            });
        }

        return Task.FromResult(_process != null);
    }

    private void ProcessOnExited(object? sender, EventArgs e)
    {
        _isPlaying = false;
        PlayStopped?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> StopSongAsync(string? newSongPath = null, bool waitForFile = false)
    {
        Pause();
        return Task.FromResult(true);
    }

    public EventHandler? PlayStarted { get; set; }
    public EventHandler? PlayPaused { get; set; }
    public EventHandler? PlayStopped { get; set; }

    public bool CanPlayMusic { get; set; } = false;

    public bool CanSetMusicPosition { get; set; } = false;

    public bool CanChangeVolume { get; set; } = false;
}