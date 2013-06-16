﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WaveBox.Model;
using Un4seen.Bass;
using WaveBox.Server.Extensions;

namespace WaveBox.Static
{
	public enum JukeboxState
	{
		Stop,
		Pause,
		Play
	}

	public static class Jukebox
	{
		private static readonly log4net.ILog logger = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public static bool IsInitialized { get; set; }

		public static JukeboxState State { get; set; }

		public static bool Repeat { get; set; }

		public static bool Random { get; set; }

		public static bool Single { get; set; }

		public static bool Consume { get; set; }

		public static int CurrentIndex { get; set; }

		private const string PLAYLIST_NAME = "jukeboxQPbjnbh2JPU5NGxhXiiQ";
		private static Playlist playlist;

		private static int? currentStream;

		static Jukebox()
		{
			playlist = new Playlist.Factory().CreatePlaylist(PLAYLIST_NAME);
			if (playlist.PlaylistId == null)
			{
				// playlist doesn't exist, so create it.
				playlist.CreatePlaylist();
			}
		}

		public static double Progress()
		{
			if (IsInitialized && currentStream != null)
			{
				long bytePosition = Bass.BASS_ChannelGetPosition(currentStream.Value, BASSMode.BASS_POS_BYTES | BASSMode.BASS_POS_DECODE);
				double seconds = Bass.BASS_ChannelBytes2Seconds(currentStream.Value, bytePosition);
				return seconds;
			}

			return 0.0;
		}

		public static float Volume()
		{
			if (IsInitialized && currentStream != null)
			{
				return Bass.BASS_GetVolume();
			}

			return 0.0f;
		}

		public static void Play()
		{
			if (currentStream != null)
			{
				Bass.BASS_Start();
				State = JukeboxState.Play;
			}
		}

		public static void Pause()
		{
			if (State == JukeboxState.Play && currentStream != 0)
			{
				Bass.BASS_Pause();
				State = JukeboxState.Pause;
			}
		}

		// Overload for pause toggle
		public static void Pause(bool toggle)
		{
			if (toggle)
			{
				if (State == JukeboxState.Pause)
				{
					Play();
				}
				else
				{
					Pause();
				}
			}
		}

		public static void Stop()
		{
			if (IsInitialized)
			{
				BassFree();
				State = JukeboxState.Stop;
			}
		}

		public static void Prev()
		{
			CurrentIndex = CurrentIndex - 1 < 0 ? 0 : CurrentIndex - 1;
			PlaySongAtIndex(CurrentIndex);
		}

		public static void Next()
		{
			CurrentIndex = CurrentIndex + 1;

			if (CurrentIndex >= playlist.PlaylistCount) 
			{
				CurrentIndex = CurrentIndex - 1;
				Stop();
			} 
			else
			{
				PlaySongAtIndex (CurrentIndex);
			}
		}

		public static void PlaySongAtIndex(int index)
		{
			IMediaItem item = playlist.MediaItemAtIndex(index);
			if (logger.IsInfoEnabled) logger.Info("Playing song: " + item.FileName);

			if (item != null)
			{
				if (item.ItemTypeId == (int)ItemType.Song)
				{
					// set the current index
					CurrentIndex = index;

					// re-initialize bass
					BassInit();
					if (logger.IsInfoEnabled) logger.Info("Re-initializing BASS");

					// create the stream
					string path = item.File().Name;
					currentStream = Bass.BASS_StreamCreateFile(path, 0, 0, BASSFlag.BASS_STREAM_PRESCAN);

					if (currentStream == 0)
					{
						if (logger.IsInfoEnabled) logger.Info("BASS failed to create stream for " + path);
					}
					else 
					{
						if (logger.IsInfoEnabled) logger.Info("Current stream handle: " + currentStream);

						SYNCPROC end = new SYNCPROC(delegate (int handle, int channel, int data, IntPtr user)
						{
							// when the stream ends, go to the next stream in the playlist.
							Next();
						});

						Bass.BASS_ChannelSetSync(currentStream.Value, BASSSync.BASS_SYNC_END, 0, end, System.IntPtr.Zero);
						Bass.BASS_ChannelPlay(currentStream.Value, true);
						State = JukeboxState.Play;
					}
				}
				// jukebox mode currently only plays songs, so skip if it's not a song.
				else
				{
					Next();
				}
			}
		}

		public static List<IMediaItem> ListOfSongs()
		{
			return playlist.ListOfMediaItems();
		}

		public static void RemoveSongAtIndex(int index)
		{
			playlist.RemoveMediaItemAtIndex(index);
		}

		public static void RemoveSongsAtIndexes(List<int> indices)
		{
			playlist.RemoveMediaItemAtIndexes(indices);
		}

		public static void MoveSong(int fromIndex, int toIndex)
		{
			playlist.MoveMediaItem(fromIndex, toIndex);
		}

		public static void AddSong(Song song)
		{
			playlist.AddMediaItem(song, true);
		}

		public static void AddSongs(List<Song> songs)
		{
			List<IMediaItem> mediaItems = new List<IMediaItem>();
			mediaItems.AddRange(songs);
			playlist.AddMediaItems(mediaItems);
		}

		public static void InsertSong(Song song, int index)
		{
			playlist.InsertMediaItem(song, index);
		}

		public static void ClearPlaylist()
		{
			playlist.ClearPlaylist();
		}

		private static void BassInit()
		{
			// if we are initializing, we want to make sure that we're not
			// already initialized.
			if (IsInitialized)
			{
				if (logger.IsInfoEnabled) logger.Info("Freeing BASS");
				BassFree();
			}

			// set the buffer to 200ms, which is the minimum.
			if (logger.IsInfoEnabled) logger.Info("Setting up BASS");
			//Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_BUFFER, Bass.BASS_GetConfig(BASSConfig.BASS_CONFIG_UPDATEPERIOD + 200));
			if (logger.IsInfoEnabled) logger.Info("BASS buffer size: " + Bass.BASS_GetConfig(BASSConfig.BASS_CONFIG_BUFFER) + "ms");

			// dsp effects for floating point math to avoid clipping
			Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_FLOATDSP, 1);

			// initialize the audio output device
			if (!Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, System.IntPtr.Zero))
			{
				if (logger.IsInfoEnabled) logger.Info("Error initializing BASS!");
			}

			else IsInitialized = true;
			
		}

		private static void BassFree()
		{
			Bass.BASS_Free();
			Bass.BASS_PluginFree(0);

			IsInitialized = false;
			State = JukeboxState.Stop;
			currentStream = 0;
		}

		private static string BassErrorCodeToString(int code)
		{
			return "";
		}
	}
}
