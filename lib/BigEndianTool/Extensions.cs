using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public static class Extensions {
    public static byte[] decryptCBC128(this byte[] orginaldata, byte[] key, byte[] iv) {
        using (System.Security.Cryptography.Aes AES = System.Security.Cryptography.Aes.Create()) {
            AES.Mode = CipherMode.CBC;
            AES.Padding = PaddingMode.None;
            AES.KeySize = 128;
            AES.Key = key.SubArray(0, 16);
            AES.IV = iv.SubArray(0, 16);
            ICryptoTransform transform = AES.CreateDecryptor();
            byte[] outputData = transform.TransformFinalBlock(orginaldata, 0, orginaldata.Length);
            return outputData;
        }
    }

    public static byte[] encryptCBC128(this byte[] orginaldata, byte[] key, byte[] iv) {
        using (System.Security.Cryptography.Aes AES = System.Security.Cryptography.Aes.Create()) {
            AES.Mode = CipherMode.CBC;
            AES.Padding = PaddingMode.None;
            AES.KeySize = 128;
            AES.Key = key.SubArray(0, 16);
            AES.IV = iv.SubArray(0, 16);
            ICryptoTransform transform = AES.CreateEncryptor();
            byte[] outputData = transform.TransformFinalBlock(orginaldata, 0, orginaldata.Length);
            return outputData;
        }
    }

    public static T[] SubArray<T>(this T[] data, int index, int length) {
        T[] result = new T[length];
        Array.Copy(data, index, result, 0, length);
        return result;
    }

    public static string ToHexString(this byte[] data) {
        return BitConverter.ToString(data).Replace("-", "");
    }

    public static byte[] ToByteArray(this string hex) {
        return Enumerable.Range(0, hex.Length)
                         .Where(x => x % 2 == 0)
                         .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                         .ToArray();
    }

    public static string ArrayToString<T>(this T[] array) {
        StringBuilder builder = new StringBuilder("[");
        for (int i = 0; i < array.GetLength(0); i++) {
            if (i != 0) builder.Append(",");
            builder.Append(array[i]);
        }
        builder.Append("]");
        return builder.ToString();
    }

    public static T ToStruct<T>(this byte[] ptr) {
        GCHandle handle = GCHandle.Alloc(ptr, GCHandleType.Pinned);
        T ret = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
        handle.Free();
        return ret;
    }

    public static byte[] StructToBytes<T>(this T obj) {
        byte[] buffer =  new byte[Marshal.SizeOf(typeof(T))];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        Marshal.StructureToPtr(obj, handle.AddrOfPinnedObject(), false);
        handle.Free();
        return buffer;
    }

    public static short ChangeEndian(this short val) {
        byte[] arr = BitConverter.GetBytes(val);
        Array.Reverse(arr);
        return BitConverter.ToInt16(arr, 0);
    }

    public static int ChangeEndian(this int val) {
        byte[] arr = BitConverter.GetBytes(val);
        Array.Reverse(arr);
        return BitConverter.ToInt32(arr, 0);
    }

    public static uint ChangeEndian(this uint val) {
        byte[] arr = BitConverter.GetBytes(val);
        Array.Reverse(arr);
        return BitConverter.ToUInt32(arr, 0);
    }

    public static long ChangeEndian(this long val) {
        byte[] arr = BitConverter.GetBytes(val);
        Array.Reverse(arr);
        return BitConverter.ToInt64(arr, 0);
    }

    public static ulong ChangeEndian(this ulong val) {
        byte[] arr = BitConverter.GetBytes(val);
        Array.Reverse(arr);
        return BitConverter.ToUInt64(arr, 0);
    }
}
