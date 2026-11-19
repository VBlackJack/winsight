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

using Microsoft.Diagnostics.Runtime;
using Winsight.Core.Models;

namespace Winsight.Core.Modules.ManagedState;

public sealed class ManagedStateReader
{
    public OperationResult Read(int processId, string typeName, string fieldName, bool requireUnique = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        using var dataTarget = DataTarget.AttachToProcess(processId, suspend: false);
        ClrInfo? clrInfo = dataTarget.ClrVersions.FirstOrDefault();
        if (clrInfo is null)
        {
            return OperationResult.Failure($"Process {processId} has no managed runtime loaded.");
        }

        using ClrRuntime runtime = clrInfo.CreateRuntime();
        ClrHeap heap = runtime.Heap;
        if (!heap.CanWalkHeap)
        {
            return OperationResult.Failure("Managed heap is not in a walkable state.");
        }

        object? matchedValue = null;
        bool matched = false;
        foreach (ClrObject obj in heap.EnumerateObjects())
        {
            if (obj.Type is null)
            {
                continue;
            }
            if (!string.Equals(obj.Type.Name, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            ClrInstanceField? field = obj.Type.Fields.FirstOrDefault(f => f.Name == fieldName);
            if (field is null)
            {
                return OperationResult.Failure($"Type '{typeName}' has no field '{fieldName}'.");
            }

            if (matched) return OperationResult.Failure($"Multiple instances of '{typeName}' exist; verification requires a unique instance.");
            matchedValue = ReadValue(obj, field, fieldName);
            matched = true;
            if (!requireUnique) return OperationResult.OkWith(matchedValue);
        }

        return matched ? OperationResult.OkWith(matchedValue) : OperationResult.Failure($"No instance of type '{typeName}' found in heap.");
    }

    private static object? ReadValue(ClrObject obj, ClrInstanceField field, string fieldName)
    {
        if (field.ElementType == ClrElementType.String)
        {
            return obj.ReadStringField(fieldName);
        }

        if (field.IsObjectReference)
        {
            ClrObject reference = obj.ReadObjectField(fieldName);
            if (reference.IsNull)
            {
                return null;
            }
            return $"{reference.Type?.Name ?? "<unknown>"} @ 0x{reference.Address:x}";
        }

        return field.ElementType switch
        {
            ClrElementType.Boolean => obj.ReadField<bool>(fieldName),
            ClrElementType.Char => obj.ReadField<char>(fieldName),
            ClrElementType.Int8 => obj.ReadField<sbyte>(fieldName),
            ClrElementType.UInt8 => obj.ReadField<byte>(fieldName),
            ClrElementType.Int16 => obj.ReadField<short>(fieldName),
            ClrElementType.UInt16 => obj.ReadField<ushort>(fieldName),
            ClrElementType.Int32 => obj.ReadField<int>(fieldName),
            ClrElementType.UInt32 => obj.ReadField<uint>(fieldName),
            ClrElementType.Int64 => obj.ReadField<long>(fieldName),
            ClrElementType.UInt64 => obj.ReadField<ulong>(fieldName),
            ClrElementType.Float => obj.ReadField<float>(fieldName),
            ClrElementType.Double => obj.ReadField<double>(fieldName),
            ClrElementType.NativeInt => obj.ReadField<long>(fieldName),
            _ => $"<unsupported element type: {field.ElementType}>",
        };
    }
}
