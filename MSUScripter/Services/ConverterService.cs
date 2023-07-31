﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MSURandomizerLibrary.Configs;
using MSUScripter.Configs;
using Track = MSUScripter.Configs.Track;

namespace MSUScripter.Services;

public class ConverterService
{
    public static bool ConvertViewModel<A, B>(A input, B output, bool recursive = true) where B : new()
    {
        var propertiesA = typeof(A).GetProperties().Where(x => x.CanWrite && x.PropertyType.Namespace?.Contains("MSU") != true).ToDictionary(x => x.Name, x => x);
        var propertiesB = typeof(B).GetProperties().Where(x => x.CanWrite && x.PropertyType.Namespace?.Contains("MSU") != true).ToDictionary(x => x.Name, x => x);
        var updated = false;

        if (propertiesA.Count != propertiesB.Count)
        {
            throw new InvalidOperationException($"Types {typeof(A).Name} and {typeof(B).Name} are not compatible");
        }

        foreach (var propA in propertiesA.Values)
        {
            if (!propertiesB.TryGetValue(propA.Name, out var propB))
            {
                continue;
            }

            if (propA.PropertyType == typeof(List<A>))
            {
                if (recursive)
                {
                    var aValue = propA.GetValue(input) as List<A>;
                    var bValue = new List<B>();
                    if (aValue != null)
                    {
                        foreach (var aSubItem in aValue)
                        {
                            var bSubItem = new B();
                            ConvertViewModel(aSubItem, bSubItem);
                            bValue.Add(bSubItem);
                        }
                    }
                    propB.SetValue(output, bValue);
                }
            }
            else
            {
                var value = propA.GetValue(input);
                var originalValue = propA.GetValue(input);
                updated |= value != originalValue;
                propB.SetValue(output, value);
            }
        }

        return updated;
    }

    public static ICollection<MsuSongMsuPcmInfo> ConvertMsuPcmTrackInfo(Track_base trackBase, string rootPath)
    {
        var outputList = new List<MsuSongMsuPcmInfo>();
        var output = new MsuSongMsuPcmInfo();
        
        var propertiesA = typeof(Track_base).GetProperties().Where(x => x.CanWrite).ToDictionary(x => x.Name.ToLower().Replace("_", ""), x => x);
        var propertiesB = typeof(MsuSongMsuPcmInfo).GetProperties().Where(x => x.CanWrite).ToDictionary(x => x.Name.ToLower().Replace("_", ""), x => x);

        foreach (var key in propertiesA.Keys.Where(x => propertiesB.Keys.Contains(x)))
        {
            var propA = propertiesA[key];
            var propB = propertiesB[key];

            var valueA = propA.GetValue(trackBase);
            propB.SetValue(output, valueA);
        }

        if (!string.IsNullOrEmpty(output.File))
        {
            output.File = GetAbsolutePath(rootPath, output.File);
        }
        
        if (trackBase is Track track)
        {
            if (track.Sub_channels?.Any() == true)
                output.SubChannels = track.Sub_channels.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();
            if (track.Sub_tracks?.Any() == true)
                output.SubTracks = track.Sub_tracks.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();

            if (track.Options?.Any() == true)
            {
                foreach (var option in track.Options.OrderBy(x => x.Option != track.Use_option))
                {
                    option.Copy(track);
                    var optionInfo = ConvertMsuPcmTrackInfo(option, rootPath).First();
                    outputList.Add(optionInfo);
                }
            }
        }
        else if (trackBase is Track_option trackOptions)
        {
            if (trackOptions.Sub_channels?.Any() == true)
                output.SubChannels = trackOptions.Sub_channels.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();
            if (trackOptions.Sub_tracks?.Any() == true)
                output.SubTracks = trackOptions.Sub_tracks.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();
        }
        else if (trackBase is Sub_track subTrack && subTrack.Sub_channels?.Any() == true)
        {
            output.SubChannels = subTrack.Sub_channels.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();
        }
        else if (trackBase is Sub_channel subChannel && subChannel.Sub_tracks?.Any() == true)
        {
            output.SubTracks = subChannel.Sub_tracks.Select(x => ConvertMsuPcmTrackInfo(x, rootPath).First()).ToList();
        }

        if (!outputList.Any())
        {
            outputList.Add(output);
        }
        
        return outputList;
    }
    
    public static Track_base ConvertMsuPcmTrackInfo(MsuSongMsuPcmInfo trackBase, bool isSubTrack, bool isSubChannel)
    {
        Track_base output;
        if (!isSubTrack && !isSubChannel)
        {
            output = new Track();
        }
        else if (isSubChannel)
        {
            output = new Sub_channel();
        }
        else if (isSubTrack)
        {
            output = new Sub_track();
        }
        else
        {
            throw new InvalidOperationException();
        }
        
        var propertiesA = typeof(MsuSongMsuPcmInfo).GetProperties().Where(x => x.CanWrite).ToDictionary(x => x.Name.ToLower().Replace("_", ""), x => x);
        var propertiesB = typeof(Track_base).GetProperties().Where(x => x.CanWrite).ToDictionary(x => x.Name.ToLower().Replace("_", ""), x => x);

        foreach (var key in propertiesA.Keys.Where(x => propertiesB.Keys.Contains(x)))
        {
            var propA = propertiesA[key];
            var propB = propertiesB[key];

            var valueA = propA.GetValue(trackBase);
            propB.SetValue(output, valueA);
        }

        if (!isSubTrack && !isSubChannel && output is Track track)
        {
            if (trackBase.SubChannels.Any())
                track.Sub_channels = trackBase.SubChannels.Select(x => ConvertMsuPcmTrackInfo(x, false, true)).Cast<Sub_channel>().ToList();
            if (trackBase.SubTracks.Any())
                track.Sub_tracks = trackBase.SubTracks.Select(x => ConvertMsuPcmTrackInfo(x, true, false)).Cast<Sub_track>().ToList();
        }
        else if (isSubChannel && output is Sub_channel subChannel && trackBase.SubTracks.Any())
        {
            subChannel.Sub_tracks = trackBase.SubTracks.Select(x => ConvertMsuPcmTrackInfo(x, true, false)).Cast<Sub_track>().ToList();
        }
        else if (isSubTrack && output is Sub_track subTrack && trackBase.SubChannels.Any())
        {
            subTrack.Sub_channels = trackBase.SubChannels.Select(x => ConvertMsuPcmTrackInfo(x, false, true)).Cast<Sub_channel>().ToList();
        }
        
        return output;
    }
    
    public static string GetAbsolutePath(string basePath, string relativePath)
    {
        if (Path.GetFullPath(relativePath) == relativePath)
        {
            return relativePath;
        }
        
        var absolute = Path.Combine(basePath, relativePath);
        return Path.GetFullPath(absolute);
    }

    public static MsuDetails ConvertMsuDetailsToMsuType(MsuDetails msuDetails, MsuType oldType, MsuType msuType, string oldPath, string newPath)
    {
        var newDetails = new MsuDetails()
        {
            PackName = msuDetails.PackName,
            PackAuthor = msuDetails.PackAuthor,
            PackVersion = msuDetails.PackVersion,
            Artist = msuDetails.Artist,
            Album = msuDetails.Album,
            MsuType = msuType.Name,
            Url = msuDetails.Url,
        };

        if (msuDetails.Tracks?.Any() != true)
        {
            return newDetails;
        }

        var conversion = msuType.Conversions[oldType];

        newDetails.Tracks = new Dictionary<string, MsuDetailsTrack>();

        var oldMsu = new FileInfo(oldPath);
        var oldBase = oldMsu.Name.Replace(oldMsu.Extension, "");
        var newMsu = new FileInfo(newPath);
        var newBase = newMsu.Name.Replace(newMsu.Extension, "");

        foreach (var track in msuDetails.Tracks!)
        {
            var oldDetails = track.Value;
            var oldTypeTrack = oldType.Tracks.FirstOrDefault(x =>
                x.YamlName == track.Key || x.YamlNameSecondary == track.Key ||
                x.Number == oldDetails.TrackNumber);

            if (oldTypeTrack == null)
            {
                continue;
            }
            
            var newTypeTrack =
                msuType.Tracks.FirstOrDefault(x =>
                    x.YamlName == track.Key || x.YamlNameSecondary == track.Key || x.Number == conversion(oldTypeTrack.Number));

            if (newTypeTrack == null)
            {
                continue;
            }

            var newTrackDetails = new MsuDetailsTrack()
            {
                TrackNumber = newTypeTrack.Number,
                Name = oldDetails.Name,
                Artist = oldDetails.Artist,
                Album = oldDetails.Album,
                FileLength = oldDetails.FileLength,
                Hash = oldDetails.Hash,
                Path = oldDetails.Path?.Replace($"{oldBase}-{oldTypeTrack.Number}", $"{newBase}-{newTypeTrack.Number}"),
                Url = oldDetails.Url,
                MsuName = oldDetails.MsuName,
                MsuAuthor = oldDetails.MsuAuthor
            };

            if (oldDetails.Alts?.Any() == true)
            {
                newTrackDetails.Alts = new List<MsuDetailsTrack>();
                foreach (var alt in oldDetails.Alts)
                {
                    newTrackDetails.Alts.Add(new MsuDetailsTrack()
                    {
                        TrackNumber = newTypeTrack.Number,
                        Name = alt.Name,
                        Artist = alt.Artist,
                        Album = alt.Album,
                        FileLength = alt.FileLength,
                        Hash = alt.Hash,
                        Path = alt.Path?.Replace($"{oldBase}-{oldTypeTrack.Number}", $"{newBase}-{newTypeTrack.Number}"),
                        Url = alt.Url,
                        MsuName = alt.MsuName,
                        MsuAuthor = alt.MsuAuthor
                    });
                }
            }

            newDetails.Tracks[newTypeTrack.YamlNameSecondary ?? newTypeTrack.YamlName!] = newTrackDetails;
        }
        
        return newDetails;
    }
}