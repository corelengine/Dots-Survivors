using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public struct EnemyTag : IComponentData
{
}

[RequireComponent(typeof(CharacterAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    private class Backer : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<EnemyTag>(entity);
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