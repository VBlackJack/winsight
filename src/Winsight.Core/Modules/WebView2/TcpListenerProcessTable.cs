// Copyright 2026 Julien Bombled
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Runtime.InteropServices;

namespace Winsight.Core.Modules.WebView2;

internal static class TcpListenerProcessTable
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int ErrorInsufficientBuffer = 122;

    public static Dictionary<int, HashSet<int>> GetListenerOwnersByPort()
    {
        Dictionary<int, HashSet<int>> ownersByPort = new();
        AddIpv4Owners(ownersByPort);
        AddIpv6Owners(ownersByPort);
        return ownersByPort;
    }

    private static void AddIpv4Owners(Dictionary<int, HashSet<int>> ownersByPort)
    {
        ReadTable(
            ownersByPort,
            AfInet,
            Marshal.SizeOf<MibTcpRowOwnerPid>(),
            rowPtr =>
            {
                MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                return new ListenerOwner(DecodePort(row.LocalPort), (int)row.OwningPid);
            });
    }

    private static void AddIpv6Owners(Dictionary<int, HashSet<int>> ownersByPort)
    {
        ReadTable(
            ownersByPort,
            AfInet6,
            Marshal.SizeOf<MibTcp6RowOwnerPid>(),
            rowPtr =>
            {
                MibTcp6RowOwnerPid row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(rowPtr);
                return new ListenerOwner(DecodePort(row.LocalPort), (int)row.OwningPid);
            });
    }

    private static void ReadTable(
        Dictionary<int, HashSet<int>> ownersByPort,
        int addressFamily,
        int rowSize,
        Func<IntPtr, ListenerOwner> readRow)
    {
        int bufferLength = 0;
        uint result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferLength,
            sort: false,
            addressFamily,
            TcpTableClass.OwnerPidListener,
            reserved: 0);
        if (result is not ErrorInsufficientBuffer and not 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref bufferLength,
                sort: false,
                addressFamily,
                TcpTableClass.OwnerPidListener,
                reserved: 0);
            if (result != 0)
            {
                return;
            }

            int count = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, sizeof(int));
            for (var i = 0; i < count; i++)
            {
                ListenerOwner owner = readRow(rowPtr);
                if (!ownersByPort.TryGetValue(owner.Port, out HashSet<int>? owners))
                {
                    owners = new HashSet<int>();
                    ownersByPort[owner.Port] = owners;
                }
                owners.Add(owner.ProcessId);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int DecodePort(uint networkOrderPort)
    {
        byte[] bytes = BitConverter.GetBytes(networkOrderPort);
        return (bytes[0] << 8) + bytes[1];
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);

    private enum TcpTableClass
    {
        OwnerPidListener = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    private readonly record struct ListenerOwner(int Port, int ProcessId);
}
