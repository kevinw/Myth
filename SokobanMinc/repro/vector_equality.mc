// minc 0.9.18, macOS arm64: `==` / `!=` on vector and matrix types compile
// but don't compare values. For every type tested (float2/3/4, int2/3/4,
// uint2/3/4, f64x2, i64x2, u64x2, i8x16, float4x4), `==` is false and `!=` is
// true, whether the operands are equal or not.
//
//   minc run vector_equality.mc
//
// Expected:
//   int2     equal: == true   != false   different: == false  != true
//   (same for every type)
//   a == a: true   a == copy: true
//
// Actual:
//   int2     equal: == false  != true    different: == false  != true
//   (same for every type)
//   a == a: true   a == copy: false
//
// `a == a` being true while `a == copy` is false suggests it compares
// operand identity rather than contents.
//
// LANGUAGE.md doesn't document comparison operators on vectors, so either
// this should compare lane-wise (all lanes equal), or it should be a compile
// error instead of a silently wrong bool. (Hit as `if cell == player { ... }`
// never firing.)

i32 main() {
    { var a = float2{1.0f, 2.0f}; var b = a; var c = float2{1.0f, 3.0f};
      print("float2   equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = float3{1.0f, 2.0f, 3.0f}; var b = a; var c = float3{1.0f, 2.0f, 4.0f};
      print("float3   equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = float4{1.0f, 2.0f, 3.0f, 4.0f}; var b = a; var c = float4{1.0f, 2.0f, 3.0f, 5.0f};
      print("float4   equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = int2{1, 2}; var b = a; var c = int2{1, 3};
      print("int2     equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = int3{1, 2, 3}; var b = a; var c = int3{1, 2, 4};
      print("int3     equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = int4{1, 2, 3, 4}; var b = a; var c = int4{1, 2, 3, 5};
      print("int4     equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = uint2{1, 2}; var b = a; var c = uint2{1, 3};
      print("uint2    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = uint3{1, 2, 3}; var b = a; var c = uint3{1, 2, 4};
      print("uint3    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = uint4{1, 2, 3, 4}; var b = a; var c = uint4{1, 2, 3, 5};
      print("uint4    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = f64x2{1.0, 2.0}; var b = a; var c = f64x2{1.0, 3.0};
      print("f64x2    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = i64x2{1, 2}; var b = a; var c = i64x2{1, 3};
      print("i64x2    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = u64x2{1, 2}; var b = a; var c = u64x2{1, 3};
      print("u64x2    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { i8[16] x; i8[16] y; y[3] = 1;
      var a = i8x16_load(&x[0]); var b = a; var c = i8x16_load(&y[0]);
      print("i8x16    equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }
    { var a = float4x4{1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f};
      var b = a;
      var c = float4x4{2.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f};
      print("float4x4 equal: == {}  != {}   different: == {}  != {}\n", a == b, a != b, a == c, a != c); }

    var a = int2{1, 2};
    var copy = a;
    print("a == a: {}   a == copy: {}\n", a == a, a == copy);
    return 0;
}
