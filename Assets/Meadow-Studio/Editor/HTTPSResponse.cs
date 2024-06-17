using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meadow.Studio
{
    [System.Serializable]
    public struct HTTPSResponse
    {
        public bool success;
        public object data;
        public string message;
        public long code;
    }
}
