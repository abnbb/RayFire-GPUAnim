using UnityEngine;
using System;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
// using Unity.Mathematics;

public class StreamHelper : MemoryStream
{
    public int capacity;
    public StreamHelper(byte[] buffer) : base(buffer) {}
    public StreamHelper() {}

    public void writeFloat(float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        base.Write(bytes, 0, 4);
    }
    public void writeFloat3(Vector3 value)
    {
        writeFloat(value.x);
        writeFloat(value.y);
        writeFloat(value.z);
    }
    public void writeWriteQuaternion(Quaternion value)
    {
        writeFloat(value.x);
        writeFloat(value.y);
        writeFloat(value.z);
        writeFloat(value.w);
    }
}