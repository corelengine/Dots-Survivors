using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;
using Unity.Rendering;
using UnityEngine.Serialization;


public struct InitializeCharacterFlag : IComponentData, IEnableableComponent
{
}

public struct CharacterMoveDirection : IComponentData
{
    public float2 Value;
}

public struct CharacterMoveSpeed : IComponentData
{
    public float Value;
}

[MaterialProperty("_FacingDirection")]
public struct FacingDirectionOverride : IComponentData
{
    public float Value;
}

public struct CharacterMaxHitPoints : IComponentData
{
    public int Value;
}

public struct CharacterCurrentHitPoints : IComponentData
{
    public int Value;
}

public struct DamageThisFrame : IBufferElementData
{
    public int Value;
}

public class CharacterAuthoring : MonoBehaviour
{
    public float moveSpeed;
    public int hitPoints;

    private class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<InitializeCharacterFlag>(entity);
            AddComponent<CharacterMoveDirection>(entity);
            AddComponent(entity, new CharacterMoveSpeed { Value = authoring.moveSpeed });
            AddComponent(entity, new FacingDirectionOverride() { Value = 1 });
            AddComponent(entity, new CharacterMaxHitPoints()
            {
                Value = authoring.hitPoints
            });
            AddComponent(entity, new CharacterCurrentHitPoints()
            {
                Value = authoring.hitPoints
            });
            AddBuffer<DamageThisFrame>(entity);
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CharacterInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mass, shouldInitialize) in SystemAPI
                     .Query<RefRW<PhysicsMass>, EnabledRefRW<InitializeCharacterFlag>>())
        {
            mass.ValueRW.InverseInertia = float3.zero;
            shouldInitialize.ValueRW = false;
        }
    }
}

public partial struct CharacterMoveSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (velocity, facingDirection, direction, speed, entity) in SystemAPI
                     .Query<RefRW<PhysicsVelocity>, RefRW<FacingDirectionOverride>, RefRO<CharacterMoveDirection>,
                         RefRO<CharacterMoveSpeed>>().WithEntityAccess())
        {
            var moveStep2d = direction.ValueRO.Value * speed.ValueRO.Value;
            velocity.ValueRW.Linear = new float3(moveStep2d, 0f);

            if (math.abs(moveStep2d.x) > 0.15f)
            {
                facingDirection.ValueRW.Value = math.sign(moveStep2d.x);
            }

            if (SystemAPI.HasComponent<PlayerTag>(entity))
            {
                var animationOverride = SystemAPI.GetComponentRW<AnimationIndexOverride>(entity);
                PlayerAnimationIndex animationType = math.lengthsq(moveStep2d) > float.Epsilon
                    ? PlayerAnimationIndex.Movement
                    : PlayerAnimationIndex.Idle;
                animationOverride.ValueRW.Value = (float)animationType;
            }
        }
    }
}

public partial struct GlobalTimeUpdateSystem : ISystem
{
    private static int _globalTimeShaderPropertyID;

    public void OnCreate(ref SystemState state)
    {
        _globalTimeShaderPropertyID = Shader.PropertyToID("_GlobalTime");
    }

    public void OnUpdate(ref SystemState state)
    {
        Shader.SetGlobalFloat(_globalTimeShaderPropertyID, (float)SystemAPI.Time.ElapsedTime);
    }
}

public partial struct ProcessDamageThisFrameSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (hitPoints, damageThisFrame, entity) in SystemAPI
                     .Query<RefRW<CharacterCurrentHitPoints>, DynamicBuffer<DamageThisFrame>>().WithPresent<DestroyEntityFlag>().WithEntityAccess())
        {
            if (damageThisFrame.IsEmpty) continue;
            foreach (var damage in damageThisFrame)
            {
                hitPoints.ValueRW.Value -= damage.Value;
            }

            damageThisFrame.Clear();

            if (hitPoints.ValueRO.Value <= 0)
            {
                SystemAPI.SetComponentEnabled<DestroyEntityFlag>(entity, true);
            }
        }
    }
}