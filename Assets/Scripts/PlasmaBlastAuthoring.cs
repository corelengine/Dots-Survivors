
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

public struct PlasmaBlastData : IComponentData
{
    public float MoveSpeed;
    public int AttackDamage;
}

public class PlasmaBlastAuthoring : MonoBehaviour
{
    public float moveSpeed;
    public int attackDamage;

    private class Baker : Baker<PlasmaBlastAuthoring>
    {
        public override void Bake(PlasmaBlastAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity,new PlasmaBlastData
            {
                MoveSpeed = authoring.moveSpeed,
                AttackDamage = authoring.attackDamage
            });
            AddComponent<DestroyEntityFlag>(entity);
            SetComponentEnabled<DestroyEntityFlag>(entity, false);
        }
    }
}

public partial struct MovePlasmaBlastSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var (transform, data) in SystemAPI.Query<RefRW<LocalTransform>, PlasmaBlastData>())
        {
            transform.ValueRW.Position += transform.ValueRO.Right() * data.MoveSpeed * deltaTime;   
        }
    }
}

[UpdateInGroup(typeof(PhysicsSystemGroup))]
[UpdateAfter(typeof(PhysicsSimulationGroup))]
[UpdateBefore(typeof(AfterPhysicsSystemGroup))]
public partial struct PlasmaBlastAttackSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SimulationSingleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var attackJob = new PlasmaBlastAttackJob
        {
            PlasmaBlastLookup = SystemAPI.GetComponentLookup<PlasmaBlastData>(true),
            EnemyLookup = SystemAPI.GetComponentLookup<EnemyTag>(true),
            DamageBufferLookup = SystemAPI.GetBufferLookup<DamageThisFrame>(),
            DestroyEntityFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>()
        };

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        state.Dependency = attackJob.Schedule(simulationSingleton, state.Dependency);
    }
}

public struct PlasmaBlastAttackJob : ITriggerEventsJob
{
    [ReadOnly] public ComponentLookup<PlasmaBlastData> PlasmaBlastLookup;
    [ReadOnly] public ComponentLookup<EnemyTag> EnemyLookup;
    
    public BufferLookup<DamageThisFrame> DamageBufferLookup;
    public ComponentLookup<DestroyEntityFlag> DestroyEntityFlagLookup;
    
    public void Execute(TriggerEvent triggerEvent)
    {
        Entity plasmaEntity;
        Entity enemyEntity;

        if (PlasmaBlastLookup.HasComponent(triggerEvent.EntityA) && EnemyLookup.HasComponent(triggerEvent.EntityB))
        {
            plasmaEntity = triggerEvent.EntityA;
            enemyEntity = triggerEvent.EntityB;
        }
        else if (PlasmaBlastLookup.HasComponent(triggerEvent.EntityB) && EnemyLookup.HasComponent(triggerEvent.EntityA))
        {
            plasmaEntity = triggerEvent.EntityB;
            enemyEntity = triggerEvent.EntityA;
        }
        else return;

        var attackDamage = PlasmaBlastLookup[plasmaEntity].AttackDamage;
        var enemyDamageBuffer = DamageBufferLookup[enemyEntity];

        enemyDamageBuffer.Add(new DamageThisFrame { Value = attackDamage });
        
        DestroyEntityFlagLookup.SetComponentEnabled(plasmaEntity, true);
    }
}