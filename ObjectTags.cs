using System.Collections.Generic;
using UnityEngine;

public class ObjectTags : MonoBehaviour
{
    //Script for tagging objects in the editor so you can identify what properties objects have such as a "Breakable" object or a "Climbable" object
    public List<string> tags;



    //Functions related to tags
    public static bool ObjHasTag(GameObject obj, string tag)
    {
        ObjectTags objectTags = obj.GetComponent<ObjectTags>();
        if (objectTags == null) return false;
        return objectTags.tags.Contains(tag);
    }

    public static void AddTagToObject(GameObject obj, string tag)
    {
        ObjectTags objectTags = obj.GetComponent<ObjectTags>();
        if (objectTags == null)
        {
            objectTags = obj.AddComponent<ObjectTags>();
        }
        if (!objectTags.tags.Contains(tag))
        {
            objectTags.tags.Add(tag);
        }
    }

    public static List<GameObject> FindObjectsWithTag(string tag, FindObjectsSortMode sortMode = FindObjectsSortMode.None)
    {
        List<GameObject> taggedObjects = new List<GameObject>();
        ObjectTags[] allObjectTags = FindObjectsByType<ObjectTags>(sortMode);
        foreach (var objectTag in allObjectTags)
        {
            if (objectTag.tags.Contains(tag))
            {
                taggedObjects.Add(objectTag.gameObject);
            }
        }
        return taggedObjects;
    }
}
