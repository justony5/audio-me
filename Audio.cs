using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioSwitch {
    public sealed class Endpoint {
        public string Id;
        public string Name;
        public int Flow;
        public override string ToString() { return Name; }
    }
    public sealed class Audio : IDisposable {
        readonly IDevices devices = (IDevices)new DeviceEnumerator();
        static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
        public List<Endpoint> List(int flow) {
            ICollection collection = null;
            var result = new List<Endpoint>();
            try {
                Check(devices.EnumAudioEndpoints(flow, 1, out collection));
                uint count; Check(collection.GetCount(out count));
                for (uint i = 0; i < count; i++) {
                    IDevice device = null; IProperties properties = null;
                    try {
                        Check(collection.Item(i, out device));
                        string id; Check(device.GetId(out id));
                        Check(device.OpenPropertyStore(0, out properties));
                        var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                        Variant value;
                        Check(properties.GetValue(ref key, out value));
                        string name;
                        try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : id; }
                        finally { PropVariantClear(ref value); }
                        result.Add(new Endpoint { Id = id, Name = name, Flow = flow });
                    } finally { Release(properties); Release(device); }
                }
            } finally { Release(collection); }
            result.Sort((a,b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name,b.Name));
            return result;
        }
        public string Default(int flow, int role) {
            IDevice device = null;
            try {
                int hr = devices.GetDefaultAudioEndpoint(flow, role, out device);
                if (hr == unchecked((int)0x80070490)) return null;
                Check(hr); string id; Check(device.GetId(out id)); return id;
            } finally { Release(device); }
        }
        public string Roles(Endpoint endpoint) {
            var labels = new List<string>();
            string[] names = { "System", "Media", "Calls" };
            for (int r = 0; r < 3; r++) if (Default(endpoint.Flow,r) == endpoint.Id) labels.Add(names[r]);
            return String.Join(" / ", labels.ToArray());
        }
        public void Apply(params Endpoint[] endpoints) {
            // Validate the whole pair before touching either direction.
            var previous = new List<Tuple<int,int,string>>();
            foreach (var endpoint in endpoints) {
                if (!List(endpoint.Flow).Exists(d => d.Id == endpoint.Id)) throw new InvalidOperationException(endpoint.Name + " is disconnected.");
                for (int r = 0; r < 3; r++) previous.Add(Tuple.Create(endpoint.Flow,r,Default(endpoint.Flow,r)));
            }
            IPolicy policy = (IPolicy)new PolicyClient();
            try {
                try {
                    foreach (var endpoint in endpoints)
                        for (int r = 0; r < 3; r++) Check(policy.SetDefaultEndpoint(endpoint.Id,r));
                    bool verified = false;
                    for (int attempt = 0; attempt < 10; attempt++) {
                        verified = true;
                        foreach (var endpoint in endpoints)
                            for (int r = 0; r < 3; r++) if (Default(endpoint.Flow,r) != endpoint.Id) verified = false;
                        if (verified) break;
                        Thread.Sleep(60);
                    }
                    if (!verified) throw new InvalidOperationException("Windows did not retain all requested defaults.");
                } catch (Exception original) {
                    bool restored = true;
                    foreach (var old in previous) {
                        try { if (old.Item3 == null) { restored = false; continue; } Check(policy.SetDefaultEndpoint(old.Item3,old.Item2)); }
                        catch { restored = false; }
                    }
                    foreach (var old in previous) {
                        try { if (Default(old.Item1,old.Item2) != old.Item3) restored = false; } catch { restored = false; }
                    }
                    throw new InvalidOperationException(original.Message + (restored ? " Previous defaults restored." : " Some previous defaults could not be restored; check Windows Sound settings."), original);
                }
            } finally { Release(policy); }
        }
        public void Dispose() { Release(devices); }
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref Variant value);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size=24)] struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevices {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out ICollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ICollection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDevice {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IProperties properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IProperties {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out Variant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref Variant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyClient { }
    // Undocumented Windows interface. Slots before SetDefaultEndpoint must retain their ABI order.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPolicy {
        [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr id, int mode, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr id);
        [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(IntPtr id, int mode, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
    }
}
