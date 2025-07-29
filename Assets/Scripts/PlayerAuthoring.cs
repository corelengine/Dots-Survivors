using System;
using TMG.Survivors;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public struct PlayerTag : IComponentData
{
}

public struct CameraTarget : IComponentData
{
    public UnityObjectRef<Transform> CameraTransform;
}

public struct InitializeCameraTargetTag : IComponentData
{
}

[MaterialProperty("_AnimationIndex")]
public struct AnimationIndexOverride : IComponentData
{
    public float Value;
}

public enum PlayerAnimationIndex : byte
{
    Movement = 0,
    Idle = 1,

    None = byte.MaxValue
}

public struct PlayerAttackData : IComponentData
{
    public Entity AttackPrefab;
    public float CooldownTime;
    public float3 DetectionSize;
    public CollisionFilter CollisionFilter;
}

public struct PlayerCooldownExpirationTimestamp : IComponentData
{
    public double Value;
}

public struct GemsCollectedCount : IComponentData
{
    public int Value;
}

public struct UpdateGemUIFlag : IComponentData, IEnableableComponent
{
}

public class PlayerAuthoring : MonoBehaviour
{
    public GameObject attackPrefab;
    public float cooldownTime;
    public float detectionSize;

    private class Baker : Baker<PlayerAuthoring>
    {
        public override void Bake(PlayerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
            AddComponent<InitializeCameraTargetTag>(entity);
            AddComponent<CameraTarget>(entity);
            AddComponent<AnimationIndexOverride>(entity);

            var enemyLayer = LayerMask.NameToLayer("Enemy");
            var enemyLayerMask = (uint)math.pow(2, enemyLayer);

            var attackCollisionFilter = new CollisionFilter
            {
                BelongsTo = uint.MaxValue,
                CollidesWith = enemyLayerMask
            };

            AddComponent(entity, new PlayerAttackData
            {
                AttackPrefab = GetEntity(authoring.attackPrefab, TransformUsageFlags.Dynamic),
                CooldownTime = authoring.cooldownTime,
                DetectionSize = new float3(authoring.detectionSize),
                CollisionFilter = attackCollisionFilter
            });
            AddComponent<PlayerCooldownExpirationTimestamp>(entity);
            AddComponent<GemsCollectedCount>(entity);
            AddComponent<UpdateGemUIFlag>(entity);
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CameraInitializationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<InitializeCameraTargetTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (CameraTargetSingleton.Instance == null) return;

        var cameraTargetTransform = CameraTargetSingleton.Instance.transform;

        var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

        foreach (var (cameraTarget, entity) in SystemAPI.Query<RefRW<CameraTarget>>()
                     .WithAll<InitializeCameraTargetTag, PlayerTag>().WithEntityAccess())
        {
            cameraTarget.ValueRW.CameraTransform = cameraTargetTransform;
            ecb.RemoveComponent<InitializeCameraTargetTag>(entity);
        }

        ecb.Playback(state.EntityManager);
    }
}

[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct MoveCameraSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, cameraTarget) in SystemAPI.Query<LocalToWorld, CameraTarget>().WithAll<PlayerTag>()
                     .WithNone<InitializeCameraTargetTag>())
        {
            cameraTarget.CameraTransform.Value.position = transform.Position;
        }
    }
}

public partial class PlayerInputSystem : SystemBase
{
    private SurvivorsInput _input;

    protected override void OnCreate()
    {
        _input = new SurvivorsInput();
        _input.Enable();
    }

    protected override void OnUpdate()
    {
        float2 currentInput = _input.Player.Move.ReadValue<Vector2>();

        foreach (var direction in SystemAPI.Query<RefRW<CharacterMoveDirection>>().WithAll<PlayerTag>())
        {
            direction.ValueRW.Value = currentInput;
        }
    }
}

public partial struct PlayerAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;

        var ecbSystem = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        var phisicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();

        foreach (var (expirationTimestamp, attackData, transform) in SystemAPI
                     .Query<RefRW<PlayerCooldownExpirationTimestamp>, PlayerAttackData, LocalTransform>())
        {
            if (expirationTimestamp.ValueRO.Value > elapsedTime) continue;

            var spawnPosition = transform.Position;
            var minDetectPosition = spawnPosition - attackData.DetectionSize;
            var maxDetectPosition = spawnPosition + attackData.DetectionSize;

            var aabbInput = new OverlapAabbInput
            {
                Aabb = new Aabb
                {
                    Min = minDetectPosition,
                    Max = maxDetectPosition
                },
                Filter = attackData.CollisionFilter
            };

            var overlapHits = new NativeList<int>(state.WorldUpdateAllocator);
            if (!phisicsWorldSingleton.OverlapAabb(aabbInput, ref overlapHits))
            {
                continue;
            }

            var maxDistanceSq = float.MaxValue;
            var closestEnemyPosition = float3.zero;

            foreach (var overlapHit in overlapHits)
            {
                var currentEnemyPosition = phisicsWorldSingleton.Bodies[overlapHit].WorldFromBody.pos;
                var distanceToPlayerSq = math.distance(spawnPosition.xy, currentEnemyPosition.xy);

                if (distanceToPlayerSq < maxDistanceSq)
                {
                    maxDistanceSq = distanceToPlayerSq;
                    closestEnemyPosition = currentEnemyPosition;
                }
            }

            var vectorToClosestEnemy = closestEnemyPosition - spawnPosition;
            var angleToClosestEnemy = math.atan2(vectorToClosestEnemy.y, vectorToClosestEnemy.x);
            var spawnOrientation = quaternion.Euler(0, 0, angleToClosestEnemy);

            var newAttack = ecb.Instantiate(attackData.AttackPrefab);

            ecb.SetComponent(newAttack, LocalTransform.FromPositionRotation(spawnPosition, spawnOrientation));

            expirationTimestamp.ValueRW.Value = elapsedTime + attackData.CooldownTime;
        }
    }
}

public partial struct UpdateGemUiSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (gemCount, shouldUpdateUI) in SystemAPI.Query<GemsCollectedCount, EnabledRefRW<UpdateGemUIFlag>>())
        {
            GameUIController.Instance.UpdateGemsCollectedText(gemCount.Value);
            shouldUpdateUI.ValueRW = false;
        }
    }
}