namespace IrradianceCacheSystem
{
    /// <summary>
    /// Volume 数据存储模式
    /// </summary>
    public enum VolumeStorageMode
    {
        /// <summary>
        /// 八叉树模式（层次化稀疏存储）
        /// </summary>
        Octree = 0,

        /// <summary>
        /// 均匀网格模式（单层平铺存储）
        /// </summary>
        UniformGrid = 1,
    }
}
