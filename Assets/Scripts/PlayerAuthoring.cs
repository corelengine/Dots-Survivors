using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

public struct PlayerTag : IComponentData { }

public struct CameraTarget : IComponentData
{
        public UnityObjectRef<Transform> CameraTransform;
}

public struct InitializeCameraTargetTag : IComponentData { }

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

public class PlayerAuthoring : MonoBehaviour
{
        private class Baker : Baker<PlayerAuthoring>
        {
                public override void Bake(PlayerAuthoring authoring)
                {
                        var entity = GetEntity(TransformUsageFlags.Dynamic);
                        AddComponent<PlayerTag>(entity);
                        AddComponent<InitializeCameraTargetTag>(entity);
                        AddComponent<CameraTarget>(entity);
                        AddComponent<AnimationIndexOverride>(entity);
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
                if(CameraTargetSingleton.Instance == null) return;

                var cameraTargetTransform = CameraTargetSingleton.Instance.transform;

                var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

                foreach (var (cameraTarget, entity) in SystemAPI.Query<RefRW<CameraTarget>>().WithAll<InitializeCameraTargetTag, PlayerTag>().WithEntityAccess())
                {
                        cameraTarget.ValueRW.CameraTransform = cameraTargetTransform;
                        ecb.RemoveComponent<InitializeCameraTargetTag>(entity);
                }
                ecb.Playback(state.EntityManager);
        }
}

[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct MovecameraSystem : ISystem
{
        public void OnUpdate(ref SystemState state)
        {
                foreach (var (transform,cameraTarget) in SystemAPI.Query<LocalToWorld, CameraTarget>().WithAll<PlayerTag>().WithNone<InitializeCameraTargetTag>())
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