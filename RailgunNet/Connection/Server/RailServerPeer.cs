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

#if SERVER
using System;
using System.Collections.Generic;

namespace Railgun
{
  /// <summary>
  /// A peer created by the server representing a connected client.
  /// </summary>
  internal class RailServerPeer
    : RailPeer<RailClientPacket, RailServerPacket>
  {
    internal event Action<RailServerPeer, IRailClientPacket> PacketReceived;

    public string Identifier { get; set; }

    internal RailServerPeer(
      IRailNetPeer netPeer,
      RailInterpreter interpreter)
      : base(
          netPeer, 
          RailConfig.CLIENT_SEND_RATE,
          interpreter)
    {
    }

    internal void Shutdown()
    {
      this.Controller.Shutdown();
    }

    internal void SendPacket(
      Tick localTick,
      IEnumerable<RailEntity> active,
      IEnumerable<RailEntity> destroyed)
    {
      RailServerPacket packet = base.PrepareSend<RailServerPacket>(localTick);
      this.Controller.Scope.PopulateDeltas(
        this.Controller, 
        localTick, 
        packet, 
        active, 
        destroyed);
      base.SendPacket(packet);

      foreach (RailStateDelta delta in packet.Sent)
        this.Controller.Scope.RegisterSent(
          delta.EntityId, 
          localTick, 
          delta.IsFrozen);
    }

    protected override void ProcessPacket(RailPacket packet, Tick localTick)
    {
      base.ProcessPacket(packet, localTick);

      RailClientPacket clientPacket = (RailClientPacket)packet;
      this.Controller.Scope.IntegrateAcked(clientPacket.View);
      this.PacketReceived?.Invoke(this, clientPacket);
    }
  }
}
#endif