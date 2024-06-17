using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class BundleInfo
{
    private string m_outputDirectory;
    private BuildAssetBundleOptions m_options;
    private BuildTarget m_buildTarget;
    private Action<string> m_onBuild;
    private string m_buildType;
    private string m_bundleType;
    public string outputDirectory
    {
        get
        {
            return m_outputDirectory;
        }
        set
        {
            m_outputDirectory = value;
        }
    }
    public BuildAssetBundleOptions options
    {
        get
        {
            return m_options;
        }
        set
        {
            m_options = value;
        }
    }
    public BuildTarget buildTarget
    {
        get
        {
            return m_buildTarget;
        }
        set
        {
            m_buildTarget = value;
        }
    }

    public Action<string> onBuild
    {
        get
        {
            return m_onBuild;
        }
        set
        {
            m_onBuild = value;
        }
    }
    public string buildType
    {
        get
        {
            return m_buildType;
        }
        set
        {
            m_buildType = value;
        }
    }
    public string bundleType
    {
        get
        {
            return m_bundleType;
        }
        set
        {
            m_bundleType = value;
        }
    }
}
