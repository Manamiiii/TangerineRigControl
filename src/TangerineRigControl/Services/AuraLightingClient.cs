// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Manamiiii

using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace TangerineRigControl.Services
{
    // Official Aura COM API. Vendor DLLs are resolved by Windows registration, never bundled.
    internal interface IAuraBackend : IDisposable
    {
        string SetEnabled(bool enabled, CancellationToken cancellation);
        void ReleaseExisting();
    }

    internal sealed class AuraLightingClient : IAuraBackend
    {
        private readonly object _gate = new object();
        private IAuraSdk2 _sdk;
        private bool _disposed;
        private bool _releaseUncertain;

        internal static bool IsSupportedType(uint type)
        {
            return type == 0x10000 || type == 0x11000 || type == 0x70000;
        }

        public string SetEnabled(bool enabled, CancellationToken cancellation)
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("AuraLightingClient");
                if (_releaseUncertain) throw new InvalidOperationException("上次 SDK 释放失败，控制权状态未知；请重启控制台后检查原厂灯效。");
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (_sdk == null)
                    {
                        var sdkType = Type.GetTypeFromProgID("aura.sdk.1", false);
                        if (sdkType == null) throw new InvalidOperationException("Aura SDK 未注册。请检查原厂 Aura 软件及服务；也可在设置中取消 SDK，使用录制方式。");
                        _sdk = (IAuraSdk2)Activator.CreateInstance(sdkType);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    _sdk.SwitchMode();
                    if (enabled)
                    {
                        Release();
                        return "华硕默认灯效控制权已恢复；是否点亮取决于默认设置。";
                    }
                    var deviceCount = 0;
                    var categories = new System.Collections.Generic.List<string>();
                    foreach (var type in new uint[] { 0x10000, 0x11000, 0x70000 })
                    {
                        var before = deviceCount;
                        var devices = _sdk.Enumerate(type);
                        try
                        {
                            var count = Convert.ToInt32(Get(devices, "Count"));
                            for (var i = 0; i < count; i++)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                var device = Get(devices, "Item", i);
                                try
                                {
                                    if (!IsSupportedType(Convert.ToUInt32(Get(device, "Type")))) continue;
                                    var lights = Get(device, "Lights");
                                    try
                                    {
                                        var lightCount = Convert.ToInt32(Get(lights, "Count"));
                                        for (var j = 0; j < lightCount; j++)
                                        {
                                            cancellation.ThrowIfCancellationRequested();
                                            var light = Get(lights, "Item", j);
                                            try { light.GetType().InvokeMember("Color", BindingFlags.SetProperty, null, light, new object[] { (uint)0 }); }
                                            finally { Free(light); }
                                        }
                                        cancellation.ThrowIfCancellationRequested();
                                        device.GetType().InvokeMember("Apply", BindingFlags.InvokeMethod, null, device, null);
                                        if (lightCount > 0) deviceCount++;
                                    }
                                    finally { Free(lights); }
                                }
                                finally { Free(device); }
                            }
                        }
                        finally { Free(devices); }
                        if (deviceCount > before) categories.Add(type == 0x10000 ? "主板" : type == 0x11000 ? "ARGB" : "内存");
                    }
                    if (deviceCount == 0) throw new InvalidOperationException("Aura SDK 未识别到可控制的主板或内存灯光。");
                    // Keep the COM session alive while off; ReleaseControl restores Armoury effects.
                    return "已向识别到的 " + string.Join("、", categories) + " 写入关灯；未列出的类别未确认。退出控制台将释放控制权。";
                }
                catch (OperationCanceledException)
                {
                    try { Release(); }
                    catch (Exception ex) { throw new InvalidOperationException("操作取消，但 SDK 控制权释放失败（" + ex.GetType().Name + "）；灯效状态未知。", ex); }
                    throw;
                }
                catch (Exception ex)
                {
                    var releaseFailed = false;
                    try { Release(); } catch { releaseFailed = true; }
                    var error = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    var reason = error is InvalidOperationException && error.Message.StartsWith("Aura SDK 未", StringComparison.Ordinal)
                        ? error.Message : "华硕 SDK 操作失败（" + error.GetType().Name + " / 0x" + error.HResult.ToString("X8") + "）。";
                    throw new InvalidOperationException(reason + (releaseFailed ? " 控制权释放失败，状态未知。" : "") + " 请检查 Aura 服务，或切换到录制方式。", error);
                }
            }
        }

        private static object Get(object value, string name, params object[] args)
        {
            return value.GetType().InvokeMember(name, BindingFlags.GetProperty, null, value, args);
        }
        private static void Free(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
        private void Release()
        {
            if (_sdk == null) return;
            var sdk = _sdk;
            try { sdk.ReleaseControl(0); _releaseUncertain = false; }
            catch { _releaseUncertain = true; throw; }
            finally { _sdk = null; Free(sdk); }
        }
        public void ReleaseExisting() { lock (_gate) { Release(); } }
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                Release();
                if (_releaseUncertain) throw new InvalidOperationException("SDK 控制权未确认释放。");
            }
        }

        [ComImport, Guid("EE69DBAE-33FF-4E45-B378-01797A59852D"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        private interface IAuraSdk2
        {
            [DispId(1)] [return: MarshalAs(UnmanagedType.IDispatch)] object Enumerate(uint type);
            [DispId(2)] void SwitchMode();
            [DispId(3)] void ReleaseControl(uint reserved);
        }
    }
}
