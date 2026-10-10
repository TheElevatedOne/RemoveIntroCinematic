// Compile-time stand-in for the game's UnityEngine.dll (unsigned, version 0.0.0.0).
// The built mod still binds to Risk of Rain 2's real UnityEngine at runtime.
namespace UnityEngine
{
    public class Object
    {
    }

    public class Component : Object
    {
    }

    public class Behaviour : Component
    {
    }

    public class MonoBehaviour : Behaviour
    {
    }
}
