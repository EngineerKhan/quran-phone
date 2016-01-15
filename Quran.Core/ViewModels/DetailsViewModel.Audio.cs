// --------------------------------------------------------------------------------------------------------------------
// <summary>
//    Defines the DetailsViewModel type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Quran.Core.Common;
using Quran.Core.Data;
using Quran.Core.Interfaces;
using Quran.Core.Properties;
using Quran.Core.Utils;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Quran.Core.ViewModels
{
    /// <summary>
    /// Define the DetailsViewModel type.
    /// </summary>
    public partial class DetailsViewModel : ViewModelWithDownload
    {
        #region Properties
        private AudioState audioPlayerState;
        public AudioState AudioPlayerState
        {
            get { return audioPlayerState; }
            set
            {
                if (value == audioPlayerState)
                    return;

                audioPlayerState = value;
                base.OnPropertyChanged(() => AudioPlayerState);
            }
        }

        private bool isLoadingAudio;
        public bool IsLoadingAudio
        {
            get { return isLoadingAudio; }
            set
            {
                if (value == isLoadingAudio)
                    return;

                isLoadingAudio = value;
                base.OnPropertyChanged(() => IsLoadingAudio);
            }
        }
        #endregion Properties

        #region Audio

        public async Task<bool> Play()
        {
            if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Playing)
            {
                // Do nothing
                return true;
            }
            else if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Paused)
            {
                QuranApp.NativeProvider.AudioProvider.Play();
                return true;
            }
            else
            {
                var bounds = QuranUtils.GetPageBounds(CurrentPageNumber);
                QuranAyah ayah = new QuranAyah
                {
                    Surah = bounds[0],
                    Ayah = bounds[1]
                };

                if (ayah.Ayah == 1 && ayah.Surah != Constants.SURA_TAWBA &&
                    ayah.Surah != Constants.SURA_FIRST)
                {
                    ayah.Ayah = 0;
                }

                return await PlayFromAyah(ayah);
            }
        }

        public void Pause()
        {
            QuranApp.NativeProvider.AudioProvider.Pause();
        }

        public void Stop()
        {
            QuranApp.NativeProvider.AudioProvider.Stop();
        }

        public void NextTrack()
        {
            var ayah = SelectedAyah;
            if (ayah != null)
            {
                if (AudioPlayerState == AudioState.Playing)
                {
                    QuranApp.NativeProvider.AudioProvider.Next();
                }
            }
        }

        public void PreviousTrack()
        {
            var ayah = SelectedAyah;
            if (ayah != null)
            {
                if (AudioPlayerState == AudioState.Playing)
                {
                    QuranApp.NativeProvider.AudioProvider.Previous();
                }
            }
        }

        public async Task<bool> PlayFromAyah(QuranAyah ayah)
        {
            int currentQari = AudioUtils.GetReciterIdByName(SettingsUtils.Get<string>(Constants.PREF_ACTIVE_QARI));
            if (currentQari == -1)
                return false;

            //var shouldRepeat = SettingsUtils.Get<bool>(Constants.PREF_AUDIO_REPEAT);
            //var repeatAmount = SettingsUtils.Get<RepeatAmount>(Constants.PREF_REPEAT_AMOUNT);
            //var repeatTimes = SettingsUtils.Get<int>(Constants.PREF_REPEAT_TIMES);
            var request = new QuranAudioTrack(currentQari, ayah, 
                SettingsUtils.Get<bool>(Constants.PREF_PREFER_STREAMING),
                SettingsUtils.Get<AudioDownloadAmount>(Constants.PREF_DOWNLOAD_AMOUNT),
                FileUtils.ScreenInfo);

            // if necessary download aya position file
            await DownloadAyahPositionFile();

            if (request.IsStreaming)
            {
                telemetry.TrackEvent("PlayAudioStreaming");
                PlayStreaming(request);
                return true;
            }
            else
            {
                telemetry.TrackEvent("PlayAudioCached");
                return await DownloadAndPlayAudioRequest(request);
            }
        }

        private void PlayStreaming(QuranAudioTrack request)
        {
            QuranApp.NativeProvider.AudioProvider.SetTrack(request);
        }

        private async Task<bool> DownloadAndPlayAudioRequest(QuranAudioTrack request)
        {
            if (request == null)
            {
                return false;
            }

            if (this.ActiveDownload.IsDownloading)
            {
                return true;
            }

            var result = await DownloadAudioRequest(request);

            if (!result)
            {
                await QuranApp.NativeProvider.ShowErrorMessageBox("Something went wrong. Unable to download audio.");
                return false;
            }
            else
            {
                QuranApp.NativeProvider.AudioProvider.SetTrack(request);
                return true;
            }
        }

        private async Task<bool> DownloadAudioRequest(QuranAudioTrack request)
        {
            bool result = true;

            // checking if need to download mp3
            var missingFiles = await AudioUtils.GetMissingFiles(request);
            if (missingFiles.Count > 0)
            {
                string url = request.GetReciter().ServerUrl;
                StorageFolder destination = await request.GetReciter().GetStorageFolder();
                result = await QuranApp.DetailsViewModel.ActiveDownload.DownloadMultipleViaHttpClient(
                    missingFiles.Select(f => Path.Combine(url, f)).ToArray(),
                    destination, Resources.loading_audio);
            }
            return result;
        }

        private async void AudioProvider_StateChanged(IAudioProvider sender, AudioPlayerPlayState e)
        {
            await UpdateAudioState();
        }

        private async void AudioProvider_TrackChanged(IAudioProvider sender, QuranAudioTrack request)
        {
            await UpdateAudioState();
        }

        public async Task UpdateAudioState()
        {
            if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Stopped ||
                QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Closed ||
                QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Buffering)
            {
                await Task.Delay(500);
                // Check if still stopped
                if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Stopped ||
                    QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Closed ||
                    QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Buffering)
                {
                    AudioPlayerState = AudioState.Stopped;
                }
            }
            else if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Paused)
            {
                AudioPlayerState = AudioState.Paused;
            }
            else if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Playing)
            {
                AudioPlayerState = AudioState.Playing;
            }

            if (QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Opening ||
                QuranApp.NativeProvider.AudioProvider.State == AudioPlayerPlayState.Buffering)
            {
                IsLoadingAudio = true;
            }
            else
            {
                IsLoadingAudio = false;
            }

            try
            {
                if (QuranApp.NativeProvider.AudioProvider.CurrentTrack != null)
                {
                    var requestAyah = QuranApp.NativeProvider.AudioProvider.CurrentTrack.GetQuranAyah();
                    var pageNumber = QuranUtils.GetPageFromAyah(requestAyah);
                    var oldPageIndex = CurrentPageIndex;
                    var newPageIndex = GetIndexFromPageNumber(pageNumber);

                    CurrentPageIndex = newPageIndex;
                    if (oldPageIndex != newPageIndex)
                    {
                        await Task.Delay(500);
                    }
                    // If bismillah set to first ayah
                    if (requestAyah.Ayah == 0)
                        requestAyah.Ayah = 1;
                    SelectedAyah = requestAyah;
                }
                else
                {
                    SelectedAyah = null;
                }
            }
            catch (Exception ex)
            {
                await QuranApp.NativeProvider.ShowErrorMessageBox(ex.Message);
                telemetry.TrackException(ex, new Dictionary<string, string> { { "Scenario", "UpdateAudioStateInViewModel" } });
            } 
        }

        #endregion
    }
}
