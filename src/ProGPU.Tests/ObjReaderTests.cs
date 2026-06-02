using System;
using System.IO;
using System.Numerics;
using Microsoft.UI.Xaml.Media.Media3D;
using Xunit;

namespace ProGPU.Tests
{
    public class ObjReaderTests : IDisposable
    {
        private readonly string _tempDir;

        public ObjReaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ProGPU_ObjReaderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public void Test_NegativeVertexIndices_RelativeOffsets()
        {
            string objContent = @"
v 0 0 0
v 1 0 0
v 1 1 0
f -3 -2 -1
";
            var bytes = System.Text.Encoding.UTF8.GetBytes(objContent);
            var model = ObjReader.LoadObj(bytes, null);

            Assert.Single(model.Parts);
            var part = model.Parts[0];
            Assert.Equal(3, part.Geometry.Positions.Length);
            Assert.Equal(3, part.Geometry.TriangleIndices.Length);
            
            Assert.Equal(0, part.Geometry.TriangleIndices[0]);
            Assert.Equal(1, part.Geometry.TriangleIndices[1]);
            Assert.Equal(2, part.Geometry.TriangleIndices[2]);
        }

        [Fact]
        public void Test_TrailingComments_Parsing()
        {
            string objContent = @"
v 0 0 0 # first vertex
v 1 0 0 # second vertex
v 1 1 0 # third vertex
f 1 2 3 # draw face
";
            var bytes = System.Text.Encoding.UTF8.GetBytes(objContent);
            var model = ObjReader.LoadObj(bytes, null);

            Assert.Single(model.Parts);
            var part = model.Parts[0];
            Assert.Equal(3, part.Geometry.Positions.Length);
            Assert.Equal(3, part.Geometry.TriangleIndices.Length);
            
            Assert.Equal(0, part.Geometry.TriangleIndices[0]);
            Assert.Equal(1, part.Geometry.TriangleIndices[1]);
            Assert.Equal(2, part.Geometry.TriangleIndices[2]);
        }

        [Fact]
        public void Test_SpacesInNamesAndFiles()
        {
            string mtlFilename = "My Premium Material.mtl";
            string objFilename = "My Model.obj";

            string mtlPath = Path.Combine(_tempDir, mtlFilename);
            string objPath = Path.Combine(_tempDir, objFilename);

            string mtlContent = @"
# Premium material with space in name
newmtl Carbon Fiber
Kd 0.1 0.1 0.1
Ks 0.8 0.8 0.8
Ns 128
Tr 0.1
illum 2
";
            string objContent = @"
mtllib My Premium Material.mtl
v 0 0 0
v 1 0 0
v 1 1 0
usemtl Carbon Fiber
f 1 2 3
";

            File.WriteAllText(mtlPath, mtlContent);
            File.WriteAllText(objPath, objContent);

            var model = ObjReader.LoadObj(objPath);

            Assert.Single(model.Parts);
            var part = model.Parts[0];
            Assert.Equal("Carbon Fiber", part.MaterialName);
            
            Assert.Equal(0.1f, part.Color.X);
            Assert.Equal(0.1f, part.Color.Y);
            Assert.Equal(0.1f, part.Color.Z);

            Assert.Equal(0.8f, part.SpecularColor.X);
            Assert.Equal(0.8f, part.SpecularColor.Y);
            Assert.Equal(0.8f, part.SpecularColor.Z);

            Assert.Equal(128f, part.Shininess);

            Assert.Equal(0.9f, part.Opacity, 2);
        }

        [Fact]
        public void Test_IlluminationModel_Matte()
        {
            string mtlFilename = "illum_test.mtl";
            string objFilename = "illum_test.obj";

            string mtlPath = Path.Combine(_tempDir, mtlFilename);
            string objPath = Path.Combine(_tempDir, objFilename);

            string mtlContent = @"
newmtl Matte Material
Kd 0.5 0.5 0.5
Ks 0.9 0.9 0.9
Ns 64
illum 1
";
            string objContent = @"
mtllib illum_test.mtl
v 0 0 0
v 1 0 0
v 1 1 0
usemtl Matte Material
f 1 2 3
";

            File.WriteAllText(mtlPath, mtlContent);
            File.WriteAllText(objPath, objContent);

            var model = ObjReader.LoadObj(objPath);

            Assert.Single(model.Parts);
            var part = model.Parts[0];

            Assert.Equal(Vector3.Zero, part.SpecularColor);
        }
    }
}
