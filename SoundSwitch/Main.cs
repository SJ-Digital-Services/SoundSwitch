﻿/********************************************************************
* Copyright (C) 2015 Jeroen Pelgrims
* Copyright (C) 2015 Antoine Aflalo
* 
* This program is free software; you can redistribute it and/or
* modify it under the terms of the GNU General Public License
* as published by the Free Software Foundation; either version 2
* of the License, or (at your option) any later version.
* 
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
********************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using AudioEndPointControllerWrapper;
using Microsoft.WindowsAPICodePack.ApplicationServices;
using SoundSwitch.Framework;
using SoundSwitch.Framework.Configuration;

namespace SoundSwitch
{
    public class Main
    {
        public delegate void AudioChangeHandler(object sender, AudioChangeEvent e);

        public delegate void ErrorHandler(object sender, ExceptionEvent e);

        public delegate void SelectedDeviceChangeHandler(object sender, DeviceListChanged e);


        public Main()
        {
            using (AppLogger.Log.DebugCall())
            {
                RegisterForRestart();
                RegisterRecovery();
            }
        }

        /// <summary>
        /// Register the configured hotkeys
        /// </summary>
        public void InitializeHotkeys()
        {
            SetHotkeyCombination(AppConfigs.Configuration.HotKeysCombinaison);
            WindowsAPIAdapter.HotKeyPressed += HandleHotkeyPress;
        }

        public HashSet<string> SelectedDevicesList
        {
            get
            {
                using (AppLogger.Log.DebugCall())
                {
                    return AppConfigs.Configuration.SelectedDeviceList;
                }
            }
        }


        public List<AudioDeviceWrapper> AvailableAudioDevices
        {
            get
            {
                using (AppLogger.Log.DebugCall())
                {
                    return
                        AudioController.getAvailableAudioDevices()
                            .Where((device => SelectedDevicesList.Contains(device.FriendlyName)))
                            .ToList();
                }
            }
        }

        public bool ChangeCommunications
        {
            get
            {
                return AppConfigs.Configuration.ChangeCommunications;
            }
            set
            {
                AppConfigs.Configuration.ChangeCommunications = value;
                AppConfigs.Configuration.Save();
            }
        }

        public string HotKeysString => AppConfigs.Configuration.HotKeysCombinaison.ToString();

        public event SelectedDeviceChangeHandler SelectedDeviceChanged;
        public event ErrorHandler ErrorTriggered;
        public event AudioChangeHandler AudioDeviceChanged;

        private void RegisterRecovery()
        {
            var settings = new RecoverySettings(new RecoveryData(SaveState, AppConfigs.Configuration), 0);
            ApplicationRestartRecoveryManager.RegisterForApplicationRecovery(settings);
            AppLogger.Log.Info("Recovery Registered");
        }

        private void RegisterForRestart()
        {
            var settings = new RestartSettings("/restart", RestartRestrictions.None);
            ApplicationRestartRecoveryManager.RegisterForApplicationRestart(settings);
            AppLogger.Log.Info("Restart Registered");
        }

        private int SaveState(object state)
        {
            using (AppLogger.Log.ErrorCall())
            {
                AppLogger.Log.Error("Saving application state");
                var settings = (SoundSwitchConfiguration) state;
                var cancelled = ApplicationRestartRecoveryManager.ApplicationRecoveryInProgress();
                if (cancelled)
                {
                    AppLogger.Log.Error("Recovery Cancelled");
                    ApplicationRestartRecoveryManager.ApplicationRecoveryFinished(false);
                    return 0;
                }
                settings.Save();
                ApplicationRestartRecoveryManager.ApplicationRecoveryFinished(true);
                AppLogger.Log.Error("Recovery Success");
                return 0;
            }
        }

        #region Events

        public class AudioChangeEvent : EventArgs
        {
            public AudioChangeEvent(AudioDeviceWrapper audioDevice)
            {
                AudioDevice = audioDevice;
            }

            public AudioDeviceWrapper AudioDevice { get; }
        }

        public class ExceptionEvent : EventArgs
        {
            public ExceptionEvent(Exception exception)
            {
                Exception = exception;
            }

            public Exception Exception { get; private set; }
        }

        public class DeviceListChanged : EventArgs
        {
            public DeviceListChanged(IEnumerable<string> seletedDevicesList)
            {
                SeletedDevicesList = seletedDevicesList;
            }

            public IEnumerable<string> SeletedDevicesList { get; private set; }
        }

        #endregion

        #region Hot keys

        /// <summary>
        ///     Sets the hotkey combination, and <see cref="ReAttachKeyboardHook">re-attaches the keyboard hook</see>.
        /// </summary>
        /// <param name="hotkeys"></param>
        public void SetHotkeyCombination(HotKeys hotkeys)
        {
            using (AppLogger.Log.InfoCall())
            {
                AppLogger.Log.Info("Unregister previous hotkeys", AppConfigs.Configuration.HotKeysCombinaison);
                WindowsAPIAdapter.UnRegisterHotKey(AppConfigs.Configuration.HotKeysCombinaison);

                if (!WindowsAPIAdapter.RegisterHotKey(hotkeys))
                {
                    AppLogger.Log.Warn("Can't register new hotkeys", hotkeys);
                    ErrorTriggered?.Invoke(this,
                        new ExceptionEvent(new Exception("Impossible to register HotKey: " + HotKeysString)));
                }
                else
                {
                    AppLogger.Log.Info("New Hotkeys registered", hotkeys);
                    AppConfigs.Configuration.HotKeysCombinaison = hotkeys;
                    AppConfigs.Configuration.Save();
                }
            }
        }


        private void HandleHotkeyPress(object sender, WindowsAPIAdapter.KeyPressedEventArgs e)
        {
            using (AppLogger.Log.DebugCall())
            {
                if (e.HotKeys != AppConfigs.Configuration.HotKeysCombinaison)
                {
                    AppLogger.Log.Debug("Not the registered Hotkeys", e.HotKeys);
                    return;
                }

                try
                {
                    CycleActiveDevice();
                }
                catch (Exception ex)
                {
                    ErrorTriggered?.Invoke(this, new ExceptionEvent(ex));
                }
            }
        }

        #endregion

        #region Misc settings

        /// <summary>
        ///     If the application runs at windows startup
        /// </summary>
        public bool RunAtStartup
        {
            get { return AutoStart.IsAutoStarted(); }
            set
            {
                using (AppLogger.Log.InfoCall())
                {
                    AppLogger.Log.Info("Set AutoStart: ", value);
                    if (value)
                    {
                        AutoStart.EnableAutoStart();
                    }
                    else
                    {
                        AutoStart.DisableAutoStart();
                    }
                }
            }
        }

        #endregion

        #region Selected devices

        /// <summary>
        ///     Sets a particular device to be enabled or not
        /// </summary>
        /// <param name="deviceName"></param>
        public void AddRemoveDevice(string deviceName)
        {
            if (SelectedDevicesList.Contains(deviceName))
            {
                SelectedDevicesList.Remove(deviceName);
            }
            else
            {
                SelectedDevicesList.Add(deviceName);
            }
            SelectedDeviceChanged?.Invoke(this, new DeviceListChanged(SelectedDevicesList));
            AppConfigs.Configuration.Save();
        }

        #endregion

        #region Active device

        /// <summary>
        ///     Attempts to set active device to the specified name
        /// </summary>
        /// <param name="device"></param>
        public bool SetActiveDevice(AudioDeviceWrapper device)
        {
            using (AppLogger.Log.InfoCall())
            {
                try
                {
                    AppLogger.Log.Info("Set Default device", device);
                    device.SetAsDefault(Role.Console);
                    if (ChangeCommunications)
                    {
                        AppLogger.Log.Info("Set Default Communication device", device);
                        device.SetAsDefault(Role.Communications);
                    }
                    AudioDeviceChanged?.Invoke(this, new AudioChangeEvent(device));
                    AppConfigs.Configuration.LastActiveDevice = device.FriendlyName;
                    AppConfigs.Configuration.Save();
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorTriggered?.Invoke(this, new ExceptionEvent(ex));
                }
                return false;
            }
        }

        /// <summary>
        ///     Cycles the active device to the next device. Returns true if succesfully switched (at least
        ///     as far as we can tell), returns false if could not successfully switch. Throws NoDevicesException
        ///     if there are no devices configured.
        /// </summary>
        public bool CycleActiveDevice()
        {
            using (AppLogger.Log.InfoCall())
            {
                var list = AvailableAudioDevices;
                switch (list.Count)
                {
                    case 0:
                        ErrorTriggered?.Invoke(this, new ExceptionEvent(new NoDevicesException()));
                        return false;
                    case 1:
                        return false;
                }
                AppLogger.Log.Info("Cycle Audio Devices", list);
                var defaultDev =
                        list.FirstOrDefault(device => device.FriendlyName == AppConfigs.Configuration.LastActiveDevice) ?? 
                        list.FirstOrDefault(device => device.IsDefault(Role.Console)) ??
                        list[0];


                var next = list.SkipWhile((device, i) => device != defaultDev).Skip(1).FirstOrDefault() ?? list[0];
                AppLogger.Log.Info("Select AudioDevice", next);
                return SetActiveDevice(next);
            }
        }

        [Serializable]
        public class NoDevicesException : InvalidOperationException
        {
            public NoDevicesException() : base("No devices to select")
            {
            }
        }

        #endregion
    }
}