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
using System.Collections.Generic;

using Reservoir;

namespace Railgun
{
  internal class Interpreter
  {
    private Pool<Snapshot> snapshotPool;
    private Pool<Image> imagePool;
    private Dictionary<int, Factory> stateFactories;

    public Interpreter(Pool<Snapshot> snapshotPool, Pool<Image> imagePool)
    {
      this.snapshotPool = snapshotPool;
      this.imagePool = imagePool;
      this.stateFactories = new Dictionary<int, Factory>();
    }

    public void AddFactory(Factory factory)
    {
      this.stateFactories[factory.StateType] = factory;
    }

    public Snapshot Decode(BitPacker bitPacker)
    {
      int count = bitPacker.Pop(Encoders.EntityCount);
      int frame = bitPacker.Pop(Encoders.Frame);

      Snapshot snapshot = this.snapshotPool.Allocate();
      snapshot.Frame = frame;

      for (int i = 0; i < count; i++)
      {
        int imageId = bitPacker.Pop(Encoders.EntityId);

        snapshot.Add(this.PopulateImage(bitPacker, imageId));
      }

      return snapshot;
    }

    public Snapshot Decode(BitPacker bitPacker, Snapshot basis)
    {
      int count = bitPacker.Pop(Encoders.EntityCount);
      int frame = bitPacker.Pop(Encoders.Frame);

      Snapshot snapshot = this.snapshotPool.Allocate();
      snapshot.Frame = frame;

      for (int i = 0; i < count; i++)
      {
        int imageId = bitPacker.Pop(Encoders.EntityId);

        Image basisImage;
        if (basis.TryGet(imageId, out basisImage))
          snapshot.Add(this.PopulateImage(bitPacker, imageId, basisImage));
        else
          snapshot.Add(this.PopulateImage(bitPacker, imageId));
      }

      this.ReconcileBasis(snapshot, basis);
      return snapshot;
    }

    private Image PopulateImage(BitPacker bitPacker, int imageId)
    {
      // This is a new image, so we expect that the state type is encoded
      int stateType = bitPacker.Pop(Encoders.StateType);

      Image image = this.imagePool.Allocate();
      State state = this.stateFactories[stateType].Allocate();
      state.Decode(bitPacker);

      image.Id = imageId;
      image.State = state;
      return image;
    }

    private Image PopulateImage(BitPacker bitPacker, int imageId, Image basis)
    {
      // This is a delta image, so we don't expect an encoded state type
      Image image = this.imagePool.Allocate();
      State state = this.stateFactories[basis.State.Type].Allocate();
      state.Decode(bitPacker, basis.State);

      image.Id = imageId;
      image.State = state;
      return image;
    }

    /// <summary>
    /// Incorporates any non-updated entities from the basis snapshot into
    /// the newly-populated snapshot.
    /// </summary>
    private void ReconcileBasis(Snapshot snapshot, Snapshot basis)
    {
      foreach (Image basisImage in basis.images)
      {
        if (snapshot.Contains(basisImage.Id) == false)
        {
          Image image = this.imagePool.Allocate();
          image.Id = basisImage.Id;
          image.State = basisImage.State.Clone();
          snapshot.Add(image);
        }
      }
    }
  }
}
