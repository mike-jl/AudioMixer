﻿using BarRaider.SdTools;
using NAudio.CoreAudioApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sentry;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace AudioMixer.Actions
{
    [PluginActionId("com.javanpoirier.audiomixer.volumeup")]
    internal class VolumeUpAction : PluginBase
    {
        private class PluginSettings
        {
            public const string VOLUME_STEP = "10";

            public static PluginSettings CreateDefaultSettings()
            {
                PluginSettings instance = new PluginSettings
                {
                    GlobalLock = true,
                    GlobalVolumeStep = VOLUME_STEP,
                    IndependantVolumeStep = VOLUME_STEP,
                    InlineControlsEnabled = true,
                };
                return instance;
            }

            [JsonProperty(PropertyName = "globalLock")]
            public bool GlobalLock { get; set; } = true;

            [JsonProperty(PropertyName = "globalVolumeStep")]
            public string GlobalVolumeStep { get; set; }

            [JsonProperty(PropertyName = "independantVolumeStep")]
            public string IndependantVolumeStep { get; set; }

            [JsonProperty(PropertyName = "inlineControlsEnabled")]
            public bool InlineControlsEnabled { get; set; }
        }

        private PluginController pluginController = PluginController.Instance;
        private System.Timers.Timer timer = new System.Timers.Timer(3000);
        private bool timerElapsed = false;
        private PluginSettings settings;

        private SDConnection connection;
        private GlobalSettings globalSettings;

        public VolumeUpAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            this.connection = connection;

            SentrySdk.AddBreadcrumb(
                message: "Initializiing VolumeUp key",
                category: "VolumeUp",
                level: BreadcrumbLevel.Info
            );

            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = PluginSettings.CreateDefaultSettings();
                SaveSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<PluginSettings>();
            }

            connection.SetImageAsync(Utils.CreateVolumeUpKey());
        }

        public override void Dispose()
        {
        }

        public override void KeyPressed(KeyPayload payload)
        {
            SentrySdk.AddBreadcrumb(
                message: "Key pressed",
                category: "VolumeUp",
                level: BreadcrumbLevel.Info
            );

            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");

            timerElapsed = false;
            timer.Elapsed += (object timerSender, ElapsedEventArgs elapsedEvent) =>
            {
                timerElapsed = true;
            };
            timer.AutoReset = false;
            timer.Start();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            SentrySdk.AddBreadcrumb(
                message: "Key released",
                category: "VolumeUp",
                level: BreadcrumbLevel.Info
            );

            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Released");

            timer.Stop();
            // If the timer of 3 seconds has passed.
            if (timerElapsed)
            {
            }
            else
            {
                try
                {
                    // TODO: Check if an action is selected first.
                    SimpleAudioVolume volume = pluginController.SelectedAction?.AudioSessions?[0]?.session?.SimpleAudioVolume;
                    if (volume == null)
                    {
                        throw new Exception("Missing volume object in plugin action. It was likely closed when active.");
                    }

                    float newVolume = 1F;
                    float volumeStep = float.Parse(settings.GlobalLock ? globalSettings.VolumeStep : settings.IndependantVolumeStep) / 100;
                    if (volume.Mute) volume.Mute = !volume.Mute;
                    else
                    {
                        newVolume = volume.Volume + volumeStep;
                        volume.Volume = newVolume > 1F ? 1F : newVolume;
                    }

                    pluginController.SelectedAction.AudioSessions.ForEach(session =>
                    {
                        session.session.SimpleAudioVolume.Volume = volume.Volume;
                        session.session.SimpleAudioVolume.Mute = volume.Mute;
                    });
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, ex.ToString());
                    SentrySdk.CaptureException(ex, scope => { scope.TransactionName = "VolumeUp"; });
                }

            }
        }

        // Global settings are received on action initialization. Local settings are only received when changed in the PI.
        public override async void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
        {
            try
            {
                // Global Settings exist
                if (payload?.Settings != null && payload.Settings.Count > 0)
                {
                    globalSettings = payload.Settings.ToObject<GlobalSettings>();
                    settings.GlobalVolumeStep = globalSettings.VolumeStep;
                    settings.InlineControlsEnabled = globalSettings.InlineControlsEnabled;
                    await InitializeSettings();
                    await SaveSettings();
                }
                else // Global settings do not exist, create new one and SAVE it
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"No global settings found, creating new object");
                    globalSettings = new GlobalSettings();
                    globalSettings.InlineControlsEnabled = true;
                    await SetGlobalSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"{GetType()} ReceivedGlobalSettings Exception: {ex}");
                SentrySdk.CaptureException(ex, scope => { scope.TransactionName = "VolumeUp"; });
            }
        }

        private Task SetGlobalSettings()
        {
            globalSettings.VolumeStep = settings.GlobalVolumeStep;
            globalSettings.InlineControlsEnabled = settings.InlineControlsEnabled;

            return Connection.SetGlobalSettingsAsync(JObject.FromObject(globalSettings));
        }

        private async Task InitializeSettings()
        {
            if (String.IsNullOrEmpty(settings.IndependantVolumeStep))
            {
                settings.IndependantVolumeStep = PluginSettings.VOLUME_STEP;
            }

            float volumeStep = float.Parse(settings.GlobalLock ? settings.GlobalVolumeStep : settings.IndependantVolumeStep);
            await connection.SetTitleAsync($"+{volumeStep}");

            await SaveSettings();
        }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            await InitializeSettings();

            await SetGlobalSettings();
            await SaveSettings();

            float volumeStep = float.Parse(settings.GlobalLock ? settings.GlobalVolumeStep : settings.IndependantVolumeStep);
            await connection.SetTitleAsync($"+{volumeStep}");
        }

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(settings));
        }

        public override void OnTick() { }
    }
}
