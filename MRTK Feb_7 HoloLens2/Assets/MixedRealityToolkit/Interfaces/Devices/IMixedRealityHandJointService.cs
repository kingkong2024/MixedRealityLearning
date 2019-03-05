﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Core.Definitions.Utilities;
using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Core.Interfaces.Devices
{
    /// <summary>
    /// Mixed Reality Toolkit device definition, used to instantiate and manage a specific device / SDK
    /// </summary>
    public interface IMixedRealityHandJointService : IMixedRealityExtensionService
    {
        Transform RequestJoint(TrackedHandJoint jointToEnable, Handedness handedness);

        /// <summary>
        /// Using an offset creates an additional game object offset at the location specified by positionOffset and rotated by rotationOffset. This is more expensive than EnableJoint(), since that function reuses the same transforms for all calls to that joint. The new transform that is not cleaned up until the scene is destroyed.
        /// </summary>
        /// <param name="jointToEnable">The base joint to be offset from.</param>
        /// <param name="handedness">Whether to use the left or right hand.</param>
        /// <param name="positionOffset">An offset in local space.</param>
        /// <param name="rotationOffset">A rotation in local space.</param>
        /// <returns>The transform of the offset joint.</returns>
        Transform CreateJointWithOffset(TrackedHandJoint jointToEnable, Handedness handedness, Vector3 positionOffset, Quaternion rotationOffset);

        bool IsHandTracked(Handedness handedness);
    }
}