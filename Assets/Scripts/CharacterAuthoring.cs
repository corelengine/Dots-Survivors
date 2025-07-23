using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Unity.Physics;


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

public class CharacterAuthoring : MonoBehaviour
{
    public float moveSpeed;

    private class Baker : Baker<CharacterAuthoring>
    {
        public override void Bake(CharacterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<InitializeCharacterFlag>(entity);
            AddComponent<CharacterMoveDirection>(entity);
            AddComponent(entity, new CharacterMoveSpeed { Value = authoring.moveSpeed });
        }
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct CharacterInitializationSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (mass, shouldInitialize) in SystemAPI.Query<RefRW<PhysicsMass>, EnabledRefRW<InitializeCharacterFlag>>())
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
        foreach (var (velocity, direction, speed) in SystemAPI
                     .Query<RefRW<PhysicsVelocity>, RefRO<CharacterMoveDirection>, RefRO<CharacterMoveSpeed>>())
        {
            var moveStep2d = direction.ValueRO.Value * speed.ValueRO.Value;
            velocity.ValueRW.Linear =  new float3(moveStep2d, 0f);
        }
    }
}