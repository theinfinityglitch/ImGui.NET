using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ImGuiNET
{
    public unsafe partial struct ImTextureRef
    {
        public ImTextureData* _TexData;
        public ulong _TexID;
    }
    public unsafe partial struct ImTextureRefPtr
    {
        public ImTextureRef* NativePtr { get; }
        public ImTextureRefPtr(ImTextureRef* nativePtr) => NativePtr = nativePtr;
        public ImTextureRefPtr(IntPtr nativePtr) => NativePtr = (ImTextureRef*)nativePtr;
        public static implicit operator ImTextureRefPtr(ImTextureRef* nativePtr) => new ImTextureRefPtr(nativePtr);
        public static implicit operator ImTextureRef* (ImTextureRefPtr wrappedPtr) => wrappedPtr.NativePtr;
        public static implicit operator ImTextureRefPtr(IntPtr nativePtr) => new ImTextureRefPtr(nativePtr);
        public ImTextureDataPtr _TexData => new ImTextureDataPtr(NativePtr->_TexData);
        public ref ulong _TexID => ref Unsafe.AsRef<ulong>(&NativePtr->_TexID);
        public void Destroy()
        {
            ImGuiNative.ImTextureRef_destroy((ImTextureRef*)(NativePtr));
        }
        public ulong GetTexID()
        {
            ulong ret = ImGuiNative.ImTextureRef_GetTexID((ImTextureRef*)(NativePtr));
            return ret;
        }
    }
}
