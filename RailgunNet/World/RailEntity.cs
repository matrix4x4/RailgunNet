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

using CommonTools;

namespace Railgun
{
  public abstract class RailEntity
  {
    public static void RegisterEntityType<TEntity, TState>(int type)
      where TEntity : RailEntity<TState>, new()
      where TState : RailState, new()
    {
      RailResource.Instance.RegisterEntityType<TEntity, TState>(type);
    }

    internal RailController Controller { get; set; }
    internal RailStateBuffer StateBuffer { get; private set; }
    internal RailStateDelta StateDelta { get; private set; }

    /// <summary>
    /// A unique network ID assigned to this entity.
    /// </summary>
    public EntityId Id { get; internal set; }

    /// <summary>
    /// The int index for the type of entity this state applies to.
    /// Set by the resource manager when creating this entity.
    /// </summary>
    internal int Type { get; private set; }

    protected internal RailWorld World { get; internal set; }
    protected internal RailState State { get; private set; }

    /// <summary>
    /// SERVER: Called when the entity has a new controller. 
    /// CLIENT: Called when local control is granted or revoked.
    /// 
    /// Always called once right after OnStart().
    /// </summary>
    protected virtual void OnControllerChanged() { }

    /// <summary>
    /// SERVER: Called before the first update tick.
    /// CLIENT: Called before the first update tick.
    /// </summary>
    protected virtual void Start() { }

    /// <summary>
    /// SERVER: Called every tick.
    /// CLIENT: Called during prediction (multiple times per frame).
    /// 
    /// Includes the most recent command available from the controller.
    /// </summary>
    internal virtual void SimulateCommand(RailCommand command) { }

    /// <summary>
    /// SERVER: Called every update tick.
    /// CLIENT: Called during prediction (multiple times per frame).
    /// 
    /// Called after SimulateCommand().
    /// </summary>
    protected virtual void Simulate() { }

    #region Client Callbacks

    #endregion

    private bool hadFirstTick;

    public bool IsPredicted
    {       
      get 
      { 
        return 
          (this.Controller != null) && 
          (RailConnection.IsServer == false);
      }
    }

    internal RailEntity()
    {
      this.Controller = null;

      this.StateBuffer = new RailStateBuffer();
      this.StateDelta = new RailStateDelta();

      this.World = null;
      this.State = null;

      this.hadFirstTick = false;
    }

    internal void Initialize(int type)
    {
      this.Type = type;
      this.State = this.AllocateState();     
    }

    internal void UpdateServer()
    {
      if (this.World != null)
      {
        if (this.hadFirstTick == false)
        {
          this.Start();
          this.OnControllerChanged();
          this.hadFirstTick = true;
        }

        if (this.Controller != null)
        {
          RailCommand command = this.Controller.LatestCommand;
          if (command != null)
            this.SimulateCommand(command);
        }
        this.Simulate();
      }
    }

    internal void UpdateClient(Tick serverTick)
    {
      if (this.World != null)
      {
        if (this.hadFirstTick == false)
        {
          this.Start();
          this.OnControllerChanged();
          this.hadFirstTick = true;
        }

        if (this.Controller != null)
          this.ForwardSimulate();
        else
          this.ReplicaSimulate(serverTick);
      }
    }

    internal void ReplicaSimulate(Tick serverTick)
    {
      this.ClearDelta();

      this.StateDelta.Update(this.StateBuffer, serverTick);
      this.State.SetDataFrom(this.StateDelta.Latest);
    }

    internal bool HasLatest(Tick serverTick)
    {
      return (this.StateBuffer.GetLatestAt(serverTick) != null);
    }

    internal void ControllerChanged()
    {
      if (this.World != null)
        this.OnControllerChanged();
    }

    internal void StoreState(Tick tick)
    {
      RailState state = this.AllocateState();
      state.SetDataFrom(this.State);
      state.Tick = tick;
      this.StateBuffer.Store(state);
    }

    private RailState CloneState(RailState state)
    {
      RailState clone = this.AllocateState();
      clone.SetDataFrom(state);
      return clone;
    }

    private RailState AllocateState()
    {
      return RailResource.Instance.AllocateState(this.Type);
    }

    #region Encoding/Decoding
    internal void EncodeState(
      BitBuffer buffer, 
      Tick latestTick, 
      Tick basisTick)
    {
      RailState basis = null;
      TickSpan span = TickSpan.INVALID;

      if (basisTick.IsValid)
      {
        basis = this.StateBuffer.Get(basisTick);
        span = TickSpan.Create(latestTick, basisTick);
      }

      // Either we're in range or we have no basis
      CommonDebug.Assert(span.IsInRange ^ (basis == null));

      // Full Encode
      if (basis == null)
      {
        // Write: [State]
        this.State.EncodeData(buffer);

        // Write: [Type]
        buffer.Push(RailEncoders.EntityType, this.Type);

        // Write: [TickSpan]
        buffer.Push(RailEncoders.TickSpan, TickSpan.OUT_OF_RANGE);

        Console.WriteLine("asdf");
      }
      // Delta Encode
      else
      {
        // Write: [State]
        this.State.EncodeData(buffer, basis);

        // No [Type] for deltas

        // Write: [TickSpan]
        buffer.Push(RailEncoders.TickSpan, span);

        Console.WriteLine(span);
      }

      // Write: [Id]
      buffer.Push(RailEncoders.EntityId, this.Id);
    }

    /// <summary>
    /// Decodes the latest state for a RailEntity and returns it. 
    /// May return null if we received bad data and need to discard the state.
    /// 
    /// Throws a BasisNotFoundException if decoding is impossible.
    /// </summary>
    internal static RailState DecodeState(
      BitBuffer buffer,
      Tick latestTick,
      IDictionary<EntityId, RailEntity> knownEntities)
    {
      // Read: [Id]
      EntityId id = buffer.Pop(RailEncoders.EntityId);

      // Read: [TickSpan]
      TickSpan span = buffer.Pop(RailEncoders.TickSpan);

      RailEntity entity;
      if (knownEntities.TryGetValue(id, out entity) == false)
        entity = null;

      // Delta decode
      if (span.IsInRange)
      {
        // No [Type] for deltas

        bool canStore = true;
        RailState basis = 
          RailEntity.GetBasis(
            entity, 
            latestTick, 
            span, 
            out canStore);

        // Read: [State]
        RailState state = RailEntity.AllocateState(entity.Type, latestTick);
        state.DecodeData(buffer, basis);

        // Write entity information
        state.EntityId = id;
        state.EntityType = entity.Type;

        if (canStore)
          return state;
        return null;
      }
      // Full decode
      else if (span.IsOutOfRange)
      {
        // Read: [Type]
        int type = buffer.Pop(RailEncoders.EntityType);

        // Read: [State]
        RailState state = RailEntity.AllocateState(type, latestTick);
        state.DecodeData(buffer);

        // Write entity information
        state.EntityId = id;
        state.EntityType = type;

        return state;
      }
      else
      {
        throw new BasisNotFoundException("Invalid span: " + span);
      }
    }

    private static RailState AllocateState(int type, Tick latest)
    {
      RailState state = RailResource.Instance.AllocateState(type);
      state.Tick = latest;
      return state;
    }

    private static RailState GetBasis(
      RailEntity entity,
      Tick latestTick,
      TickSpan span,
      out bool isValid)
    {
      if (entity == null)
        throw new BasisNotFoundException("No entity found");

      isValid = true;
      Tick basisTick = Tick.Create(latestTick, span);
      RailState basis = entity.StateBuffer.Get(basisTick);

      if (basis == null)
      {
        basis = entity.StateBuffer.Latest;
        if (basis == null)
        {
          throw new BasisNotFoundException(
            "No basis or latest for id " +
            entity.Id +
            " on tick " +
            basisTick);
        }

        CommonDebug.LogWarning(
          "Missing basis, using latest and discarding for id " +
          entity.Id +
          " on tick " +
          basisTick);
        isValid = false;
      }

      return basis;
    }
    #endregion

    #region Smoothing
    public T GetSmoothedValue<T>(
      float frameDelta, 
      RailSmoother<T> smoother)
    {
      if ((this.StateDelta == null) || (this.StateDelta.Latest == null))
        return default(T);

      // If we're predicting, advance to the prediction tick. This is
      // hacky in that it assumes that we'll only ever have a one-tick 
      // difference between any two states in the delta when doing prediction.
      Tick currentTick = this.World.Tick;
      if (this.StateDelta.Latest.IsPredicted)
        currentTick = this.StateDelta.Latest.Tick;

      if (this.StateDelta.Next != null)
      {
        return smoother.Smooth(
          frameDelta,
          currentTick,
          this.StateDelta.Latest,
          this.StateDelta.Next);
      }
      else if (this.StateDelta.Prior != null)
      {
        return smoother.Smooth(
          frameDelta,
          currentTick,
          this.StateDelta.Prior,
          this.StateDelta.Latest);
      }
      else
      {
        return smoother.Access(this.StateDelta.Latest);
      }
    }
    #endregion

    #region Prediction
    private void ForwardSimulate()
    {
      if (this.StateDelta == null)
        return;

      this.ClearDelta();

      RailState latest = this.CloneState(this.StateBuffer.Latest);
      latest.IsPredicted = true;
      latest.Tick = this.World.Tick;
      this.StateDelta.Set(null, latest, null);
      this.State.SetDataFrom(latest);
      this.ApplyCommands();
    }

    private void ClearDelta()
    {
      RailState prior = this.StateDelta.Prior;
      RailState latest = this.StateDelta.Latest;
      RailState next = this.StateDelta.Next;

      if ((prior != null) && prior.IsPredicted)
        RailPool.Free(prior);
      if ((latest != null) && latest.IsPredicted)
        RailPool.Free(latest);
      if ((next != null) && next.IsPredicted)
        RailPool.Free(next);

      this.StateDelta.Clear();
    }

    private void ApplyCommands()
    {
      int offset = 1;
      foreach (RailCommand command in this.Controller.OutgoingCommands)
      {
        this.SimulateCommand(command);
        this.Simulate();
        this.PushDelta(offset);

        offset++;
      }
    }

    private void PushDelta(int offset)
    {
      RailState predicted = this.CloneState(this.State);
      predicted.Tick = this.World.Tick + offset;
      predicted.IsPredicted = true;

      RailState popped = this.StateDelta.Push(predicted);
      if ((popped != null) && popped.IsPredicted)
        RailPool.Free(popped);
    }
    #endregion

    #region DEBUG
    public virtual string DEBUG_FormatDebug() 
    {
      string output = "[";
      foreach (RailState state in this.StateBuffer.Values)
        output += state.Tick + ":" + state.DEBUG_FormatDebug() + ",";
      output = output.Remove(output.Length - 1, 1) + "] (";

      if (this.StateDelta != null)
      {
        if (this.StateDelta.Prior != null)
          output += this.StateDelta.Prior.Tick;
        output += ",";
        if (this.StateDelta.Latest != null)
          output += this.StateDelta.Latest.Tick;
        output += ",";
        if (this.StateDelta.Next != null)
          output += this.StateDelta.Next.Tick;
        output += ")";
      }

      return output;
    }
    #endregion
  }

  /// <summary>
  /// Handy shortcut class for auto-casting the internal state.
  /// </summary>
  public abstract class RailEntity<TState> : RailEntity
    where TState : RailState, new()
  {
    public new TState State { get { return (TState)base.State; } }
  }

  /// <summary>
  /// Handy shortcut class for auto-casting the internal state and command.
  /// </summary>
  public abstract class RailEntity<TState, TCommand> : RailEntity<TState>
    where TState : RailState, new()
    where TCommand : RailCommand
  {
    internal override void SimulateCommand(RailCommand command)
    {
      this.SimulateCommand((TCommand)command);
    }

    protected virtual void SimulateCommand(TCommand command) { }
  }
}
