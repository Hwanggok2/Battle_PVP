using UnityEngine;

public static class JumpPhysics
{
    public static float Integrate(ref float velocity, float gravity, float deltaTime)
    {
        float displacement = velocity * deltaTime - .5f * gravity * deltaTime * deltaTime;
        velocity -= gravity * deltaTime;
        return displacement;
    }
}
