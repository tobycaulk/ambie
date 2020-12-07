﻿using AmbientSounds.Models;
using Microsoft.Toolkit.Diagnostics;
using System;
using System.Collections.Generic;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using Windows.System;
using UwpMediaPlaybackState = Windows.Media.Playback.MediaPlaybackState;
using Windows.Storage;
using System.Threading.Tasks;

#nullable enable

namespace AmbientSounds.Services.Uwp
{
    /// <summary>
    /// Service that controls a media player.
    /// </summary>
    public sealed class MediaPlayerService : IMediaPlayerService
    {
        /// <inheritdoc/>
        public event EventHandler? NewSoundPlayed;

        /// <inheritdoc/>
        public event EventHandler<MediaPlaybackState>? PlaybackStateChanged;

        private readonly MediaPlayer _player;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly MediaPlaybackList _playbackList;

        public MediaPlayerService()
        {
            _player = new MediaPlayer { IsLoopingEnabled = true };
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _playbackList = new MediaPlaybackList { AutoRepeatEnabled = true };

            _player.PlaybackSession.PlaybackStateChanged += PlaybackSession_PlaybackStateChanged;
        }

        private void PlaybackSession_PlaybackStateChanged(MediaPlaybackSession sender, object args)
        {
            // This event is triggered by the media player object running in a background thread.
            // The dispatcher is required to avoid exceptions when this event is used by other
            // viewmodels to update bindable properties. We assume an instance of this service will
            // always be created on a UI thread (from code behind), so we use a dispatcher queue.
            _dispatcherQueue.TryEnqueue(() => PlaybackStateChanged?.Invoke(this, PlaybackState));
        }

        /// <inheritdoc/>
        public Sound? Current { get; private set; }

        /// <inheritdoc/>
        public double Volume
        {
            get => _player.Volume * 100;
            set
            {
                if (value == _player.Volume) return;
                else _player.Volume = value / 100d;
            }
        }

        /// <inheritdoc/>
        public MediaPlaybackState PlaybackState
        {
            get
            {
                return _player.PlaybackSession.PlaybackState switch
                {
                    UwpMediaPlaybackState.None => MediaPlaybackState.Stopped,
                    UwpMediaPlaybackState.Opening => MediaPlaybackState.Opening,
                    UwpMediaPlaybackState.Buffering => MediaPlaybackState.Opening,
                    UwpMediaPlaybackState.Playing => MediaPlaybackState.Playing,
                    UwpMediaPlaybackState.Paused => MediaPlaybackState.Paused,
                    _ => ThrowHelper.ThrowArgumentException<MediaPlaybackState>("Invalid playback state")
                };
            }
        }

        /// <inheritdoc/>
        public async Task Initialize(IList<Sound> sounds)
        {
            if (sounds == null || sounds.Count == 0)
                return;

            foreach (var s in sounds)
            {
                MediaSource mediaSource;
                if (Uri.IsWellFormedUriString(s.FilePath, UriKind.Absolute))
                {
                    // sound path is packaged and can be read as URI.
                    mediaSource = MediaSource.CreateFromUri(new Uri(s.FilePath));
                }
                else if (s.FilePath != null && s.FilePath.Contains(ApplicationData.Current.LocalFolder.Path))
                {
                    // sound path is likely a file saved in local folder
                    StorageFile file = await StorageFile.GetFileFromPathAsync(s.FilePath);
                    mediaSource = MediaSource.CreateFromStorageFile(file);
                }
                else
                {
                    throw new InvalidOperationException("Unrecognized file path " + s.FilePath);
                }

                var item = new MediaPlaybackItem(mediaSource);
                ApplyDisplayProperties(item, s);
                _playbackList.Items.Add(item);
            }

            _player.Source = _playbackList;
        }

        /// <inheritdoc/>
        public void Play(Sound s, int index)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.FilePath))
            {
                return;
            }

            if (s == Current)
            {
                if (PlaybackState == MediaPlaybackState.Playing || PlaybackState == MediaPlaybackState.Opening) _player.Pause();
                else _player.Play();
            }
            else
            {
                // we assume the sound list is always the same order as the playlist
                _playbackList.MoveTo((uint)index);
                _player.Play();
                Current = s;
                NewSoundPlayed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        public void Play()
        {
            _player.Play();
        }

        /// <inheritdoc/>
        public void Pause()
        {
            _player.Pause();
        }

        /// <summary>
        /// Set data in System Media Transport Controls.
        /// </summary>
        private void ApplyDisplayProperties(MediaPlaybackItem item, Sound s)
        {
            var props = item.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = s.Name ?? s.Id;
            if (!string.IsNullOrWhiteSpace(s.ImagePath) && Uri.IsWellFormedUriString(s.ImagePath, UriKind.Absolute))
            {
                props.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(s.ImagePath));
            }
            item.ApplyDisplayProperties(props);
        }
    }
}
