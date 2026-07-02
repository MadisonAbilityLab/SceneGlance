using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Newtonsoft.Json;

public class SpriteManager : MonoBehaviour
{
    [Serializable] public struct SpriteTag
    {
        public string tag;
        public Sprite sprite;
    }
    [SerializeField] List<SpriteTag> spriteTags = new List<SpriteTag>();
    Dictionary<string, Sprite> spriteTagDict = new Dictionary<string, Sprite>();
    static SpriteManager instance;
    public static SpriteManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SpriteManager>();
            }
            return instance;
        }
    }
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        Dictionary<string, string> spriteDict = new Dictionary<string, string>();

        foreach (SpriteTag spriteTag in spriteTags)
        {
            spriteTagDict[spriteTag.tag] = spriteTag.sprite;
            spriteDict[spriteTag.tag] = spriteTag.sprite.name;
        }

        // if in Unity editor, write the dictionary to a file
        // so that it can be loaded in the future
        if (Application.isEditor)
        {
            string path = Application.persistentDataPath + "/spriteDict.json";
            string json = JsonConvert.SerializeObject(spriteDict, Formatting.Indented);
            // Debug.Log(json);
            File.WriteAllText(path, json);
            Debug.Log("Sprite dictionary written to " + path);
        }
    }
    Sprite LookUp(string tag)
    {
        if (spriteTagDict.TryGetValue(tag, out Sprite cached))
            return cached;

        Sprite sprite = Resources.Load<Sprite>("Icons/" + tag.Replace(' ', '-'));
        spriteTagDict[tag] = sprite; // cache null too to avoid repeated Resources.Load
        return sprite;
    }
    static public Sprite GetSprite(string tag)
    {
        if (Instance == null)
        {
            return null;
        }
        return Instance.LookUp(tag);
    }
}
