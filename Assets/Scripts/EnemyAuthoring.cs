using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

public struct EnemyTag : IComponentData
{
}

public struct EnemyAttackData : IComponentData
{
    public int HitPoints;
    public float CooldownTime;
}

public struct EnemyCooldownExpirationTimestamp : IComponentData, IEnableableComponent
{
    public double Value;
}

public struct GemPrefab : IComponentData
{
    public Entity Value;
}

[RequireComponent(typeof(CharacterAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public int attackDamage;
    public float cooldownTime;
    public GameObject gemPrefab;

    private class Backer : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<EnemyTag>(entity);
            AddComponent(entity, new EnemyAttackData()
            {
                HitPoints = authoring.attackDamage,
                CooldownTime = authoring.cooldownTime
            });
            AddComponent<EnemyCooldownExpirationTimestamp>(entity);
            SetComponentEnabled<EnemyCooldownExpirationTimestamp>(entity, false);
            AddComponent(entity, new GemPrefab
            {
                Value = GetEntity(authoring.gemPrefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}

public partial struct EnemyMoveToPlayerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position.xy;
        EnemyMoveToPlayerJob moveToPlayerJob = new EnemyMoveToPlayerJob
        {
            PlayerPosition = playerPosition
        };

        state.Dependency = moveToPlayerJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile, WithAll(typeof(EnemyTag))]
public partial struct EnemyMoveToPlayerJob : IJobEntity
{
    public float2 PlayerPosition;

    private void Execute(ref CharacterMoveDirection direction, in LocalTransform transform)
    {
        var vectorToPlayer = PlayerPosition - transform.Position.xy;
        direction.Value = math.normalize(vectorToPlayer);
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct EnemyAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var elapsedTime = SystemAPI.Time.ElapsedTime;
        foreach (var (expirationTimestamp, cooldownEnabled) in SystemAPI
                     .Query<EnemyCooldownExpirationTimestamp, EnabledRefRW<EnemyCooldownExpirationTimestamp>>())
        {
            if (expirationTimestamp.Value > elapsedTime) continue;
            cooldownEnabled.ValueRW = false;
        }

        var attackJob = new EnemyAttackJob
        {
            PlayerLookup = SystemAPI.GetComponentLookup<PlayerTag>(true),
            AttackDataLookup = SystemAPI.GetComponentLookup<EnemyAttackData>(true),
            CooldownLookup = SystemAPI.GetComponentLookup<EnemyCooldownExpirationTimestamp>(),
            DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
            ElapseTime = elapsedTime
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
    }
}

[BurstCompile]
public struct EnemyAttackJob : ICollisionEventsJob
{
    [ReadOnly] public ComponentLookup<PlayerTag> PlayerLookup;
    [ReadOnly] public ComponentLookup<EnemyAttackData> AttackDataLookup;
    public ComponentLookup<EnemyCooldownExpirationTimestamp> CooldownLookup;

    public BufferLookup<DamageThisFrame> DamageBufferLookup;

    public double ElapseTime;

    public void Execute(CollisionEvent collisionEvent)
    {
        Entity playerEntity, enemyEntity;

        if (PlayerLookup.HasComponent(collisionEvent.EntityA) && AttackDataLookup.HasComponent(collisionEvent.EntityB))
        {
            playerEntity = collisionEvent.EntityA;
            enemyEntity = collisionEvent.EntityB;
        }
        else if (PlayerLookup.HasComponent(collisionEvent.EntityB) &&
                 AttackDataLookup.HasComponent(collisionEvent.EntityA))
        {
            playerEntity = collisionEvent.EntityB;
            enemyEntity = collisionEvent.EntityA;
        }
        else
        {
            return;
        }

        if (CooldownLookup.IsComponentEnabled(enemyEntity)) return;

        EnemyAttackData attackData = AttackDataLookup[enemyEntity];

        CooldownLookup[enemyEntity] = new EnemyCooldownExpirationTimestamp
            { Value = ElapseTime + attackData.CooldownTime };
        CooldownLookup.SetComponentEnabled(enemyEntity, true);

        DamageBufferLookup[playerEntity].Add(new DamageThisFrame
        {
            Value = attackData.HitPoints
        });
    }
}