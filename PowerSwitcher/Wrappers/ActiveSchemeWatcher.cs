using System;
using System.Runtime.InteropServices;

namespace PowerSwitcher.Wrappers
{
    /// <summary>
    /// Calls back whenever the active power plan changes, including changes made outside this app
    /// (Control Panel, powercfg, other tools). The callback runs on a system thread pool thread.
    /// </summary>
    public class ActiveSchemeWatcher : IDisposable
    {
        static readonly Guid GUID_ACTIVE_POWERSCHEME = new Guid("31f9f286-5084-42fe-b720-2b0264993763");
        const uint DEVICE_NOTIFY_CALLBACK = 2;

        readonly Action activeSchemeChanged;
        readonly DeviceNotifyCallbackRoutine callback; // Field so the GC doesn't collect the delegate while Windows holds it.
        IntPtr subscribeParameters = IntPtr.Zero;
        IntPtr registrationHandle = IntPtr.Zero;

        public ActiveSchemeWatcher(Action activeSchemeChanged)
        {
            this.activeSchemeChanged = activeSchemeChanged;
            callback = onNotification;

            subscribeParameters = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceNotifySubscribeParameters>());
            Marshal.StructureToPtr(new DeviceNotifySubscribeParameters
            {
                Callback = Marshal.GetFunctionPointerForDelegate(callback),
                Context = IntPtr.Zero
            }, subscribeParameters, false);

            var settingGuid = GUID_ACTIVE_POWERSCHEME;
            var errCode = PowerSettingRegisterNotification(ref settingGuid, DEVICE_NOTIFY_CALLBACK, subscribeParameters, out registrationHandle);
            if (errCode != 0)
            {
                Marshal.FreeHGlobal(subscribeParameters);
                subscribeParameters = IntPtr.Zero;
                throw new PowerSwitcherWrappersException($"PowerSettingRegisterNotification() failed with code {errCode}");
            }
        }

        private uint onNotification(IntPtr context, uint type, IntPtr setting)
        {
            activeSchemeChanged();
            return 0;
        }

        public void Dispose()
        {
            if (registrationHandle != IntPtr.Zero)
            {
                PowerSettingUnregisterNotification(registrationHandle);
                registrationHandle = IntPtr.Zero;
            }

            if (subscribeParameters != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(subscribeParameters);
                subscribeParameters = IntPtr.Zero;
            }
        }

        #region DLL imports

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceNotifySubscribeParameters
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingRegisterNotification(ref Guid settingGuid, uint flags, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSettingUnregisterNotification(IntPtr registrationHandle);

        #endregion
    }
}
