using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct EnemySpawnData : IComponentData
{
    public Entity EnemyPrefab;
    public float SpawnInterval;
    public float SpawnDistance;
}

public struct EnemySpawnState : IComponentData
{
    public float SpawnTimer;
    public Random Random;
}

public class EnemySpawnerAuthoring : MonoBehaviour
{
    public GameObject enemyPrefab;
    public float spawnInterval;
    public float spawnDistance;
    public uint randomSeed;
    
    private class Baker: Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new EnemySpawnData
            {
                EnemyPrefab = GetEntity(authoring.enemyPrefab, TransformUsageFlags.Dynamic),
                SpawnDistance = authoring.spawnDistance,
                SpawnInterval = authoring.spawnInterval,
            });
            AddComponent(entity, new EnemySpawnState
            {
                SpawnTimer = 0f,
                Random = Random.CreateFromIndex(authoring.randomSeed)
            });
        }
    }
}

public partial struct EnemySpawnSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<BeginInitializationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var deltaTime = SystemAPI.Time.DeltaTime;
        var ecbSystem = SystemAPI.GetSingleton<BeginInitializationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;
        
        foreach (var (spawnState, spawnData) in SystemAPI.Query<RefRW<EnemySpawnState>, EnemySpawnData>())
        {
            spawnState.ValueRW.SpawnTimer -= deltaTime;
            if(spawnState.ValueRW.SpawnTimer > 0) continue;
            
            spawnState.ValueRW.SpawnTimer = spawnData.SpawnInterval;

            var newEnemy = ecb.Instantiate(spawnData.EnemyPrefab);
            var spawnAngle = spawnState.ValueRW.Random.NextFloat(0f, math.TAU);
            var spawnPoint = new float3
            {
                x = math.sin(spawnAngle),
                y = math.cos(spawnAngle),
                z = 0f
            };
            spawnPoint *= spawnData.SpawnDistance;
            spawnPoint += playerPosition;
            
            ecb.SetComponent(newEnemy, LocalTransform.FromPosition(spawnPoint));

        }
    }
}