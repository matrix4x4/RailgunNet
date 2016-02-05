﻿/*
 *  RailgunNet - A Client/Server Network State-Synchronization Layer for Games
 *  Copyright (c) 2016 - Alexander Shoulson - http://ashoulson.com
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty. In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *  
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections;
using System.Collections.Generic;

namespace Railgun
{
  /// <summary>
  /// Responsible for encoding and decoding snapshots.
  /// 
  /// HostPacket encoding order:
  /// | BASIS TICK | ----- SNAPSHOT DATA ----- |
  /// 
  /// Snapshot encoding order:
  /// | TICK | IMAGE COUNT | ----- IMAGE ----- | ----- IMAGE ----- | ...
  /// 
  /// Image encoding order:
  /// If new: | ID | TYPE | ----- STATE DATA ----- |
  /// If old: | ID | ----- STATE DATA ----- |
  /// 
  /// </summary>
  internal class Interpreter
  {
    private BitBuffer bitBuffer;
    private List<Image> newImages;

    internal Interpreter()
    {
      this.bitBuffer = new BitBuffer();
      this.newImages = new List<Image>();
    }

    public byte[] Encode(
      Snapshot snapshot)
    {
      this.bitBuffer.Clear();

      // Write: [Snapshot Data]
      this.EncodeSnapshot(snapshot);

      // Write: [Basis Tick]
      this.bitBuffer.Push(Encoders.Tick, Clock.INVALID_TICK);

      return this.bitBuffer.StoreBytes();
    }

    public byte[] Encode(
      Snapshot snapshot, 
      Snapshot basis)
    {
      this.bitBuffer.Clear();
      
      // Write: [Snapshot Data]
      this.EncodeSnapshot(snapshot, basis);

      // Write: [Basis Tick]
      this.bitBuffer.Push(Encoders.Tick, basis.Tick);

      return this.bitBuffer.StoreBytes();
    }

    public Snapshot Decode(
      byte[] data,
      RingBuffer<Snapshot> basisBuffer)
    {
      this.bitBuffer.ReadBytes(data);

      // Read: [Basis Tick]
      int basisTick = bitBuffer.Pop(Encoders.Tick);

      // Read: [Snapshot]
      Snapshot result;
      if (basisTick != Clock.INVALID_TICK)
        result = this.DecodeSnapshot(basisBuffer.Get(basisTick));
      else
        result = this.DecodeSnapshot();

      RailgunUtil.Assert(this.bitBuffer.BitsUsed == 0);
      return result;
    }

    #region Snapshot Encode/Decode
    private void EncodeSnapshot(
      Snapshot snapshot)
    {
      foreach (Image image in snapshot.GetValues())
      {
        // Write: [Image Data]
        this.EncodeImage(image);

        // Write: [Id]
        this.bitBuffer.Push(Encoders.EntityId, image.Id);
      }

      // Write: [Count]
      this.bitBuffer.Push(Encoders.EntityCount, snapshot.Count);

      // Write: [Tick]
      this.bitBuffer.Push(Encoders.Tick, snapshot.Tick);
    }

    private void EncodeSnapshot(
      Snapshot snapshot, 
      Snapshot basis)
    {
      int count = 0;

      foreach (Image image in snapshot.GetValues())
      {
        // Write: [Image Data]
        Image basisImage;
        if (basis.TryGet(image.Id, out basisImage))
        {
          if (this.EncodeImage(image, basisImage) == false)
            continue;
        }
        else
        {
          this.EncodeImage(image);
        }

        // We may not write every state
        count++;

        // Write: [Id]
        this.bitBuffer.Push(Encoders.EntityId, image.Id);
      }

      // Write: [Count]
      this.bitBuffer.Push(Encoders.EntityCount, count);

      // Write: [Tick]
      this.bitBuffer.Push(Encoders.Tick, snapshot.Tick);
    }

    private Snapshot DecodeSnapshot()
    {
      this.newImages.Clear();

      // Read: [Tick]
      int tick = this.bitBuffer.Pop(Encoders.Tick);

      // Read: [Count]
      int count = this.bitBuffer.Pop(Encoders.EntityCount);

      Snapshot snapshot = ResourceManager.Instance.AllocateSnapshot();
      snapshot.Tick = tick;

      for (int i = 0; i < count; i++)
      {
        // Read: [Id]
        int imageId = this.bitBuffer.Pop(Encoders.EntityId);

        // Read: [Image Data]
        snapshot.Add(this.DecodeImage(imageId));
      }

      return snapshot;
    }

    private Snapshot DecodeSnapshot(
      Snapshot basis)
    {
      this.newImages.Clear();

      // Read: [Tick]
      int tick = this.bitBuffer.Pop(Encoders.Tick);

      // Read: [Count]
      int count = this.bitBuffer.Pop(Encoders.EntityCount);

      Snapshot snapshot = ResourceManager.Instance.AllocateSnapshot();
      snapshot.Tick = tick;

      for (int i = 0; i < count; i++)
      {
        // Read: [Id]
        int imageId = this.bitBuffer.Pop(Encoders.EntityId);

        // Read: [Image Data]
        Image basisImage;
        if (basis.TryGet(imageId, out basisImage))
          snapshot.Add(this.DecodeImage(imageId, basisImage));
        else
          snapshot.Add(this.DecodeImage(imageId));
      }

      this.ReconcileBasis(snapshot, basis);
      return snapshot;
    }
    #endregion

    #region Image Encode/Decode
    private void EncodeImage(
      Image image)
    {
      // Write: [State Data]
      image.State.Encode(this.bitBuffer);

      // Write: [Type]
      this.bitBuffer.Push(Encoders.StateType, image.State.Type);
    }

    private bool EncodeImage(
      Image image, 
      Image basis)
    {
      // Write: [State Data]
      return image.State.Encode(this.bitBuffer, basis.State);

      // (No type identifier for delta images)
    }

    private Image DecodeImage(
      int imageId)
    {
      // Read: [Type]
      int stateType = this.bitBuffer.Pop(Encoders.StateType);

      Image image = ResourceManager.Instance.AllocateImage();
      State state = ResourceManager.Instance.AllocateState(stateType);

      // Read: [State Data]
      state.Decode(this.bitBuffer);

      image.Id = imageId;
      image.State = state;

      this.newImages.Add(image);
      return image;
    }

    private Image DecodeImage(
      int imageId, 
      Image basis)
    {
      // (No type identifier for delta images)

      Image image = ResourceManager.Instance.AllocateImage();
      State state = ResourceManager.Instance.AllocateState(basis.State.Type);

      // Read: [State Data]
      state.Decode(this.bitBuffer, basis.State);

      image.Id = imageId;
      image.State = state;
      return image;
    }
    #endregion

    #region Internals
    /// <summary>
    /// Returns the new images created during a decode.
    /// Only valid immediately after a decode.
    /// </summary>
    internal IList<Image> GetNewImages()
    {
      return this.newImages.AsReadOnly();
    }

    /// <summary>
    /// Incorporates any non-updated entities from the basis snapshot into
    /// the newly-populated snapshot.
    /// </summary>
    private void ReconcileBasis(Snapshot snapshot, Snapshot basis)
    {
      foreach (Image basisImage in basis.GetValues())
        if (snapshot.Contains(basisImage.Id) == false)
          snapshot.Add(basisImage.Clone());
    }
    #endregion
  }
}
