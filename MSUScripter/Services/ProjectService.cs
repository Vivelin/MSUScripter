﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MSURandomizerLibrary.Configs;
using MSURandomizerLibrary.Services;
using MSUScripter.Configs;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MSUScripter.Services;

public class ProjectService
{
    private readonly IMsuTypeService _msuTypeService;
    private readonly IMsuLookupService _msuLookupService;
    private readonly IMsuDetailsService _msuDetailsService;
    private readonly AudioMetadataService _audioMetadataService;
    private readonly SettingsService _settingsService;
    private readonly ILogger<ProjectService> _logger;
    
    public ProjectService(IMsuTypeService msuTypeService, IMsuLookupService msuLookupService, IMsuDetailsService msuDetailsService, ILogger<ProjectService> logger, AudioMetadataService audioMetadataService, SettingsService settingsService)
    {
        _msuTypeService = msuTypeService;
        _msuLookupService = msuLookupService;
        _msuDetailsService = msuDetailsService;
        _logger = logger;
        _audioMetadataService = audioMetadataService;
        _settingsService = settingsService;
    }
    
    public void SaveMsuProject(MsuProject project)
    {
        project.LastSaveTime = DateTime.Now;
        var serializer = new SerializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .Build();
        var yaml = serializer.Serialize(project);
        File.WriteAllText(project.ProjectFilePath, yaml);
        _settingsService.AddRecentProject(project);
    }

    public MsuProject? LoadMsuProject(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var project = deserializer.Deserialize<MsuProject>(yaml);
        project.ProjectFilePath = path;
        project.MsuType = _msuTypeService.GetMsuType(project.MsuTypeName) ?? throw new InvalidOperationException();

        if (project.MsuType == _msuTypeService.GetSMZ3LegacyMSUType() || project.MsuType == _msuTypeService.GetSMZ3MsuType())
        {
            project.BasicInfo.IsSmz3Project = true;
        }

        _settingsService.AddRecentProject(project);
        return project;
    }

    public MsuProject NewMsuProject(string projectPath, string msuTypeName, string msuPath, string? msuPcmTracksJsonPath, string? msuPcmWorkingDirectory)
    {
        var msuType = _msuTypeService.GetMsuType(msuTypeName) ?? throw new InvalidOperationException("Invalid MSU Type");
        
        var project = new MsuProject()
        {
            ProjectFilePath = projectPath,
            MsuType = msuType,
            MsuTypeName = msuType.DisplayName,
            MsuPath = msuPath,
        };

        project.BasicInfo.MsuType = project.MsuType.Name;
        project.BasicInfo.Game = project.MsuType.Name;
        
        foreach (var track in project.MsuType.Tracks.OrderBy(x => x.Number))
        {
            project.Tracks.Add(new MsuTrackInfo()
            {
                TrackNumber = track.Number,
                TrackName = track.Name
            });
        }

        if (File.Exists(msuPath))
        {
            ImportMsu(project, msuPath);
        }

        if (!string.IsNullOrEmpty(msuPcmTracksJsonPath) && File.Exists(msuPcmTracksJsonPath))
        {
            ImportMsuPcmTracksJson(project, msuPcmTracksJsonPath, msuPcmWorkingDirectory);
        }
        
        if (msuType == _msuTypeService.GetSMZ3LegacyMSUType() || msuType == _msuTypeService.GetSMZ3MsuType())
        {
            project.BasicInfo.IsSmz3Project = true;
            project.BasicInfo.CreateSplitSmz3Script = true;
        }

        SaveMsuProject(project);

        return project;
    }

    public void ImportMsu(MsuProject project, string msuPath)
    {
        var msu = _msuLookupService.LoadMsu(msuPath, project.MsuType);

        if (msu == null)
            return;

        if (msu.MsuType != project.MsuType && msu.MsuType != null)
        {
            ConvertProjectMsuType(project, msu.MsuType);
        }

        project.BasicInfo.PackName = msu.Name;
        project.BasicInfo.PackCreator = msu.Creator;
        project.BasicInfo.PackVersion = msu.Version;

        if (msu.Tracks.Select(x => x.Artist).Distinct().Count() == 1)
        {
            project.BasicInfo.Artist = msu.Tracks.First().Artist;
        }
        
        if (msu.Tracks.Select(x => x.Album).Distinct().Count() == 1)
        {
            project.BasicInfo.Album = msu.Tracks.First().Album;
        }
        
        if (msu.Tracks.Select(x => x.Url).Distinct().Count() == 1)
        {
            project.BasicInfo.Url = msu.Tracks.First().Url;
        }

        foreach (var track in msu.Tracks)
        {
            if (track.IsCopied) continue;
            var projectTrack = project.Tracks.FirstOrDefault(x => x.TrackNumber == track.Number);
            if (projectTrack == null) continue;
            var song = projectTrack.Songs.FirstOrDefault(x => x.OutputPath == track.Path);
            if (song == null)
            {
                song = new MsuSongInfo()
                {
                    TrackNumber = track.Number,
                    TrackName = track.TrackName,
                    SongName = track.SongName,
                    Artist = track.Artist,
                    Album = track.Album,
                    Url = track.Url,
                    OutputPath = track.Path,
                    IsAlt = track.IsAlt
                };
                projectTrack.Songs.Add(song);
            }
        }
    }

    public void ConvertProjectMsuType(MsuProject project, MsuType newMsuType, bool swapPcmFiles = false)
    {
        if (!project.MsuType.IsCompatibleWith(newMsuType) && project.MsuType != newMsuType)
            return;

        var oldType = project.MsuType;
        project.MsuType = newMsuType;
        project.MsuTypeName = newMsuType.Name;

        var conversion = project.MsuType.Conversions[oldType];
        
        var msu = new FileInfo(project.MsuPath);
        var basePath = msu.FullName.Replace(msu.Extension, "");
        var baseName = msu.Name.Replace(msu.Extension, "");

        HashSet<string> swappedFiles = new HashSet<string>();
        var newTracks = new List<MsuTrackInfo>();
        foreach (var oldTrack in project.Tracks)
        {
            var newTrackNumber = conversion(oldTrack.TrackNumber);

            if (oldTrack.TrackNumber == newTrackNumber)
            {
                newTracks.Add(oldTrack);
                continue;
            }
            
            var newMsuTypeTrack = newMsuType.Tracks.FirstOrDefault(x => x.Number == newTrackNumber);
            if (newMsuTypeTrack == null) continue;

            var newSongs = new List<MsuSongInfo>();
            foreach (var oldSong in oldTrack.Songs)
            {
                var songBaseName = new FileInfo(oldSong.OutputPath).Name;
                if (!songBaseName.StartsWith($"{baseName}-{oldTrack.TrackNumber}"))
                    continue;
                
                var newSong = new MsuSongInfo()
                {
                    TrackNumber = newTrackNumber,
                    TrackName = newMsuTypeTrack.Name,
                    SongName = oldSong.SongName,
                    Artist = oldSong.Artist,
                    Album = oldSong.Album,
                    Url = oldSong.Url,
                    OutputPath = oldSong.OutputPath.Replace($"{basePath}-{oldTrack.TrackNumber}",
                        $"{basePath}-{newTrackNumber}"),
                    IsAlt = oldSong.IsAlt,
                    MsuPcmInfo = oldSong.MsuPcmInfo
                };
                
                newSongs.Add(newSong);

                if (swapPcmFiles && File.Exists(oldSong.OutputPath) && !swappedFiles.Contains(newSong.OutputPath))
                {
                    if (File.Exists(newSong.OutputPath))
                    {
                        _logger.LogInformation("{New} <=> {Old}", newSong.OutputPath, oldSong.OutputPath);
                        swappedFiles.Add(newSong.OutputPath);
                        swappedFiles.Add(oldSong.OutputPath);
                        File.Move(newSong.OutputPath, newSong.OutputPath + ".tmp");
                        File.Move(oldSong.OutputPath, oldSong.OutputPath + ".tmp");
                        File.Move(newSong.OutputPath + ".tmp", oldSong.OutputPath);
                        File.Move(oldSong.OutputPath + ".tmp", newSong.OutputPath);
                    }
                    else
                    {
                        _logger.LogInformation("{New} <== {Old}", newSong.OutputPath, oldSong.OutputPath);
                        File.Move(oldSong.OutputPath, newSong.OutputPath );
                    }
                }
            }
            
            newTracks.Add(new MsuTrackInfo()
            {
                TrackNumber = newTrackNumber,
                TrackName = newMsuTypeTrack.Name,
                Songs = newSongs
            });
        }

        project.Tracks = newTracks;
    }

    public ICollection<MsuProject> GetSmz3SplitMsuProjects(MsuProject project, out Dictionary<string, string> convertedPaths, out string? error)
    {
        var toReturn = new List<MsuProject>();
        convertedPaths = new Dictionary<string, string>();
        
        if (project.MsuType != _msuTypeService.GetSMZ3LegacyMSUType() && project.MsuType != _msuTypeService.GetSMZ3MsuType())
        {
            error = "Invalid MSU Type";
            return toReturn;
        }

        if (string.IsNullOrEmpty(project.BasicInfo.MetroidMsuPath) ||
            string.IsNullOrEmpty(project.BasicInfo.ZeldaMsuPath))
        {
            error = "Missing Metroid or Zelda MSU path";
            return toReturn;
        }

        var msuType = _msuTypeService.GetMsuType("Super Metroid") ??
                      throw new InvalidOperationException("Super Metroid MSU Type not found");
        toReturn.Add(InternalGetSmz3MsuProject(project, msuType, project.BasicInfo.MetroidMsuPath, convertedPaths));

        msuType = _msuTypeService.GetMsuType("The Legend of Zelda: A Link to the Past") ??
                  throw new InvalidOperationException("A Link to the Past MSU Type not found");
        toReturn.Add(InternalGetSmz3MsuProject(project, msuType, project.BasicInfo.ZeldaMsuPath, convertedPaths));

        error = null;
        return toReturn;
    }

    private MsuProject InternalGetSmz3MsuProject(MsuProject project, MsuType msuType, string newMsuPath, Dictionary<string, string> convertedPaths)
    {
        var basicInfo = new MsuBasicInfo();
        ConverterService.ConvertViewModel(project.BasicInfo, basicInfo);

        var conversion = msuType.Conversions[project.MsuType];

        var trackConversions = project.Tracks
            .Select(x => (x.TrackNumber, conversion(x.TrackNumber)))
            .Where(x => msuType.ValidTrackNumbers.Contains(x.Item2));

        var newTracks = new List<MsuTrackInfo>();

        var oldMsuFile = new FileInfo(project.MsuPath);
        var oldMsuBaseName = oldMsuFile.Name.Replace(oldMsuFile.Extension, "");
        var newMsuFile = new FileInfo(newMsuPath);
        var newMsuBaseName = newMsuFile.Name.Replace(newMsuFile.Extension, "");
        var folder = oldMsuFile.DirectoryName ?? "";

        foreach (var trackNumbers in trackConversions)
        {
            var oldTrackNumber = trackNumbers.TrackNumber;
            var newTrackNumber = trackNumbers.Item2;
            var trackName = msuType.Tracks.First(x => x.Number == newTrackNumber).Name;

            if (project.Tracks.First(x => x.TrackNumber == oldTrackNumber).Songs.Count > 1)
            {
                convertedPaths[Path.Combine(folder, $"{oldMsuBaseName}-{oldTrackNumber}_Original.pcm")] =
                    Path.Combine(folder, $"{newMsuBaseName}-{newTrackNumber}_Original.pcm");    
            }

            var newSongs = new List<MsuSongInfo>();
            foreach (var song in project.Tracks.First(x => x.TrackNumber == oldTrackNumber).Songs)
            {
                var newSong = new MsuSongInfo();
                ConverterService.ConvertViewModel(song, newSong);
                newSong.TrackNumber = newTrackNumber;
                newSong.TrackName = trackName;
                newSong.OutputPath =
                    song.OutputPath.Replace($"{oldMsuBaseName}-{oldTrackNumber}", $"{newMsuBaseName}-{newTrackNumber}");

                convertedPaths[song.OutputPath] = newSong.OutputPath;

                if (!File.Exists(newSong.OutputPath) && File.Exists(song.OutputPath))
                {
                    NativeMethods.CreateHardLink(newSong.OutputPath, song.OutputPath, IntPtr.Zero);
                }
                
                newSongs.Add(newSong);
            }
            
            newTracks.Add(new MsuTrackInfo()
            {
                TrackNumber = newTrackNumber,
                TrackName = trackName,
                Songs = newSongs
            });
        }
        
        return new MsuProject()
        {
            MsuPath = newMsuPath,
            MsuTypeName = msuType.Name,
            MsuType = msuType,
            BasicInfo = basicInfo,
            Tracks = newTracks
        };
    }

    public void RemoveProjectPcms(MsuProject project)
    {
        foreach (var song in project.Tracks.SelectMany(t => t.Songs))
        {
            if (File.Exists(song.OutputPath))
            {
                File.Delete(song.OutputPath);
            }
        }
    }

    public void CreateSmz3SplitScript(MsuProject smz3Project, Dictionary<string, string> convertedPaths)
    {
        var testTrack = smz3Project.Tracks.First(x => x.TrackNumber > 100 && x.Songs.Any()).TrackNumber;
        var msu = new FileInfo(smz3Project.MsuPath);
        var folder = msu.DirectoryName ?? "";
        var testPath = msu.FullName.Replace(msu.Extension, $"-{testTrack}.pcm");

        var sbIsCombined = new StringBuilder();
        var sbIsSplit = new StringBuilder();
        
        foreach (var conversion in convertedPaths)
        {
            var combinedPath = Path.GetRelativePath(folder, conversion.Key);
            var splitPath = Path.GetRelativePath(folder, conversion.Value);

            sbIsCombined.AppendLine($"\tIF EXIST \"{combinedPath}\" ( RENAME \"{combinedPath}\" \"{splitPath}\" )");
            sbIsSplit.AppendLine($"\tIF EXIST \"{splitPath}\" ( RENAME \"{splitPath}\" \"{combinedPath}\" )");
        }

        var sbTotal = new StringBuilder();
        sbTotal.AppendLine($"IF EXIST \"{testPath}\" (");
        sbTotal.Append(sbIsCombined);
        sbTotal.AppendLine(") ELSE (");
        sbTotal.Append(sbIsSplit);
        sbTotal.Append(")");
        
        File.WriteAllText(Path.Combine(folder, "!Split_Or_Combine_SMZ3_ALttP_SM_MSUs.bat"), sbTotal.ToString());
    }

    public void ImportMsuPcmTracksJson(MsuProject project, string jsonPath, string? msuPcmWorkingDirectory)
    {
        var data = File.ReadAllText(jsonPath);
        var msuPcmData = JsonConvert.DeserializeObject<MsuPcmPlusPlusConfig>(data);

        if (string.IsNullOrEmpty(msuPcmWorkingDirectory))
        {
            msuPcmWorkingDirectory = new FileInfo(jsonPath).DirectoryName!;
        }

        project.BasicInfo.PackName = msuPcmData.Pack;
        project.BasicInfo.Artist = msuPcmData.Artist;
        project.BasicInfo.Game = msuPcmData.Game;
        project.BasicInfo.Normalization = msuPcmData.Normalization;
        project.BasicInfo.Dither = msuPcmData.Dither;

        var msuFileInfo = new FileInfo(project.MsuPath);
        var msuDirectory = msuFileInfo.DirectoryName!;
        var msuName = msuFileInfo.Name.Replace(msuFileInfo.Extension, "");

        foreach (var track in msuPcmData.Tracks.GroupBy(x => x.Track_number))
        {
            var trackNumber = track.First().Track_number;
            var projectTrack = project.Tracks.FirstOrDefault(x => x.TrackNumber == trackNumber);
            if (projectTrack == null) continue;

            var msuPcmInfo = track.SelectMany(x => ConverterService.ConvertMsuPcmTrackInfo(x, msuPcmWorkingDirectory)).ToList();
            var songs = projectTrack.Songs.OrderBy(x => x.IsAlt).ToList();
            
            for (var i = 0; i < msuPcmInfo.Count; i++)
            {
                string trackPath;
                
                if (!string.IsNullOrEmpty(msuPcmInfo[i].Output))
                {
                    trackPath = ConverterService.GetAbsolutePath(msuPcmWorkingDirectory, msuPcmInfo[i].Output!);
                }
                else if (i == 0)
                {
                    trackPath = Path.Combine(msuDirectory, $"{msuName}-{trackNumber}.pcm");
                }
                else
                {
                    trackPath = Path.Combine(msuDirectory, $"{msuName}-{trackNumber}_alt{i}.pcm");
                }

                AudioMetadata? metadata = null;
                var files = msuPcmInfo[i].GetFiles();
                if (files.Count == 1)
                {
                    metadata = _audioMetadataService.GetAudioMetadata(files.First());
                }

                if (i < songs.Count)
                {
                    var song = songs[i];
                    song.MsuPcmInfo = msuPcmInfo[i];

                    if (metadata?.HasData == true)
                    {
                        if (string.IsNullOrEmpty(song.SongName) || song.SongName.StartsWith("Track #"))
                        {
                            song.SongName = metadata.SongName;
                        }

                        if (string.IsNullOrEmpty(song.Artist))
                        {
                            song.Artist = metadata.Artist;
                        }
                        
                        if (string.IsNullOrEmpty(song.Album))
                        {
                            song.Album = metadata.Album;
                        }
                        
                        if (string.IsNullOrEmpty(song.Url))
                        {
                            song.Url = metadata.Url;
                        }
                    }
                }
                else
                {
                    projectTrack.Songs.Add(new MsuSongInfo()
                    {
                        TrackNumber = trackNumber,
                        TrackName = projectTrack.TrackName,
                        SongName = metadata?.SongName,
                        Artist = metadata?.Artist,
                        Album = metadata?.Album,
                        Url = metadata?.Url,
                        OutputPath = trackPath,
                        IsAlt = i != 0,
                        MsuPcmInfo = msuPcmInfo[i]
                    });
                }
            }
        }
    }

    public void ExportMsuRandomizerYaml(MsuProject project)
    {
        var msuFile = new FileInfo(project.MsuPath);
        var msuDirectory = msuFile.Directory!;

        var tracks = new List<MSURandomizerLibrary.Configs.Track>();

        foreach (var projectTrack in project.Tracks)
        {
            foreach (var projectSong in projectTrack.Songs)
            {
                tracks.Add(new MSURandomizerLibrary.Configs.Track(
                    trackName: projectTrack.TrackName,
                    number: projectTrack.TrackNumber,
                    songName: projectSong.SongName ?? "",
                    path: projectSong.OutputPath,
                    artist: projectSong.Artist,
                    album: projectSong.Album,
                    url: projectSong.Url,
                    isAlt: projectSong.IsAlt
                ));
            }
        }

        var msu = new Msu()
        {
            MsuType = project.MsuType,
            Name = project.BasicInfo.PackName ?? msuDirectory.Name,
            Creator = project.BasicInfo.PackCreator,
            Version = project.BasicInfo.PackVersion,
            FolderName = msuDirectory.Name,
            FileName = msuFile.Name,
            Path = project.MsuPath,
            Album = project.BasicInfo.Album,
            Artist = project.BasicInfo.Artist,
            Url = project.BasicInfo.Url,
            Tracks = tracks
        };

        var yamlPath = msuFile.FullName.Replace(msuFile.Extension, ".yml");
        _msuDetailsService.SaveMsuDetails(msu, yamlPath, out var error);
    }

    public void CreateAltSwapperFile(MsuProject project, ICollection<MsuProject>? otherProjects)
    {
        if (project.Tracks.All(x => !x.Songs.Any())) return;
        
        var msuPath = new FileInfo(project.MsuPath).DirectoryName;

        if (string.IsNullOrEmpty(msuPath)) return;

        var sb = new StringBuilder();

        var trackCombos = project.Tracks.Where(t => t.Songs.Count > 1)
            .Select(t => (t.Songs.First(s => !s.IsAlt), t.Songs.First(s => s.IsAlt))).ToList();

        if (otherProjects != null)
        {
            foreach (var otherProject in otherProjects)
            {
                trackCombos.AddRange(otherProject.Tracks.Where(t => t.Songs.Count > 1)
                    .Select(t => (t.Songs.First(s => !s.IsAlt), t.Songs.First(s => s.IsAlt))));
            }
        }
        
        foreach (var combo in trackCombos)
        {
            var basePath = Path.GetRelativePath(msuPath!, combo.Item1.OutputPath);
            var baseAltPath = basePath.Replace($"-{combo.Item1.TrackNumber}.pcm",
                $"-{combo.Item1.TrackNumber}_Original.pcm");
            var altSongPath = Path.GetRelativePath(msuPath!, combo.Item2.OutputPath);

            sb.AppendLine($"IF EXIST \"{baseAltPath}\" (");
            sb.AppendLine($"\tRENAME \"{basePath}\" \"{altSongPath}\"");
            sb.AppendLine($"\tRENAME \"{baseAltPath}\" \"{basePath}\"");
            sb.AppendLine($") ELSE IF EXIST \"{altSongPath}\" (");
            sb.AppendLine($"\tRENAME \"{basePath}\" \"{baseAltPath}\"");
            sb.AppendLine($"\tRENAME \"{altSongPath}\" \"{basePath}\"");
            sb.AppendLine($")");
            sb.AppendLine();
        }

        var text = sb.ToString();
        File.WriteAllText(Path.Combine(msuPath, "!Swap_Alt_Tracks.bat"), text);
    }
}