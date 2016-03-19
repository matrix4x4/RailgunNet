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

using Railgun;
using UnityEngine;

public class DemoState : RailState<DemoState>
{
  // TODO: This class is the sort of thing that would be great to code-
  // generate, but since there's only a couple of them at most the 
  // complexity hasn't seemed to be worth it so far...

  #region Flags
  private const uint FLAG_X = 0x01;
  private const uint FLAG_Y = 0x02;
  private const uint FLAG_ANGLE = 0x04;
  private const uint FLAG_STATUS = 0x08;

  internal const uint FLAG_ALL =
    FLAG_X |
    FLAG_Y |
    FLAG_ANGLE |
    FLAG_STATUS;

  protected override int FlagBitsUsed { get { return 4; } }
  #endregion

  protected override uint GetDirtyFlags(DemoState basis)
  {
    return
      (DemoMath.CoordinatesEqual(this.X, basis.X) ? 0 : FLAG_X) |
      (DemoMath.CoordinatesEqual(this.Y, basis.Y) ? 0 : FLAG_Y) |
      (DemoMath.AnglesEqual(this.Angle, basis.Angle) ? 0 : FLAG_ANGLE) |
      (this.Status == basis.Status ? 0 : FLAG_STATUS);
  }

  // These should be properties, but we can't pass properties by ref
  public int ArchetypeId;
  public int UserId;
  public float X;
  public float Y;
  public float Angle;
  public int Status;

  protected override void ResetData()
  {
    this.ArchetypeId = 0;
    this.UserId = 0;
    this.X = 0.0f;
    this.Y = 0.0f;
    this.Angle = 0.0f;
    this.Status = 0;
  }

  protected override void SetDataFrom(DemoState other)
  {
    this.ArchetypeId = other.ArchetypeId;
    this.UserId = other.UserId;
    this.X = other.X;
    this.Y = other.Y;
    this.Angle = other.Angle;
    this.Status = other.Status;
  }

  protected override void EncodeImmutableData(BitBuffer buffer)
  {
    buffer.Write(DemoEncoders.ArchetypeId, this.ArchetypeId);
    buffer.Write(DemoEncoders.UserId, this.UserId);
  }

  protected override void DecodeImmutableData(BitBuffer buffer)
  {
    this.ArchetypeId = buffer.Read(DemoEncoders.ArchetypeId);
    this.UserId = buffer.Read(DemoEncoders.UserId);
  }

  protected override void EncodeMutableData(BitBuffer buffer, uint flags)
  {
    buffer.WriteIf(flags, FLAG_X, DemoEncoders.Coordinate, this.X);
    buffer.WriteIf(flags, FLAG_Y, DemoEncoders.Coordinate, this.Y);
    buffer.WriteIf(flags, FLAG_ANGLE, DemoEncoders.Angle, this.Angle);
    buffer.WriteIf(flags, FLAG_STATUS, DemoEncoders.Status, this.Status);
  }

  protected override void DecodeMutableData(BitBuffer buffer, uint flags)
  {
    buffer.ReadIf(flags, FLAG_X, DemoEncoders.Coordinate, ref this.X);
    buffer.ReadIf(flags, FLAG_Y, DemoEncoders.Coordinate, ref this.Y);
    buffer.ReadIf(flags, FLAG_ANGLE, DemoEncoders.Angle, ref this.Angle);
    buffer.ReadIf(flags, FLAG_STATUS, DemoEncoders.Status, ref this.Status);
  }

  protected override void EncodeControllerData(BitBuffer buffer)
  {
    buffer.Write(8, 255);
  }

  protected override void DecodeControllerData(BitBuffer buffer)
  {
    buffer.Read(8);
  }

  protected override void ResetControllerData()
  {
  }

  #region DEBUG
  public override string DEBUG_FormatDebug()
  {
    return "(" + this.X + "," + this.Y + ")";
  }
  #endregion
}