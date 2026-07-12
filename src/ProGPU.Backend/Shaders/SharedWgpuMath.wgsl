// Algorithm: Solve real quadratic and cubic equations with stable analytic branches for curve intersections.
// Time complexity: O(1) arithmetic per solver call.
// Space complexity: O(1) function-local storage.
fn solve_quadratic(a: f32, b: f32, c: f32, roots: ptr<function, array<f32, 2>>, root_count: ptr<function, u32>) {
    if (abs(a) < 0.00001) {
        if (abs(b) > 0.00001) {
            (*roots)[0] = -c / b;
            *root_count = 1u;
        } else {
            *root_count = 0u;
        }
    } else {
        let d = b * b - 4.0 * a * c;
        if (d < 0.0) {
            *root_count = 0u;
        } else if (d == 0.0) {
            (*roots)[0] = -b / (2.0 * a);
            *root_count = 1u;
        } else {
            let sqrt_d = sqrt(d);
            (*roots)[0] = (-b - sqrt_d) / (2.0 * a);
            (*roots)[1] = (-b + sqrt_d) / (2.0 * a);
            *root_count = 2u;
        }
    }
}

fn cbrt(x: f32) -> f32 {
    if (x < 0.0) {
        return -pow(-x, 1.0 / 3.0);
    }
    return pow(x, 1.0 / 3.0);
}

fn solve_cubic(a_in: f32, b_in: f32, c_in: f32, d_in: f32, roots: ptr<function, array<f32, 3>>, root_count: ptr<function, u32>) {
    if (abs(a_in) < 0.00001) {
        var quad_roots = array<f32, 2>(0.0, 0.0);
        var quad_count = 0u;
        solve_quadratic(b_in, c_in, d_in, &quad_roots, &quad_count);
        *root_count = quad_count;
        for (var i = 0u; i < quad_count; i = i + 1u) {
            (*roots)[i] = quad_roots[i];
        }
        return;
    }

    let a = b_in / a_in;
    let b = c_in / a_in;
    let c = d_in / a_in;

    let p = b - a * a / 3.0;
    let q = c - a * b / 3.0 + 2.0 * a * a * a / 27.0;

    let D = q * q / 4.0 + p * p * p / 27.0;

    if (D > 0.0) {
        let sqrt_D = sqrt(D);
        let u = cbrt(-q / 2.0 + sqrt_D);
        let v = cbrt(-q / 2.0 - sqrt_D);
        (*roots)[0] = u + v - a / 3.0;
        *root_count = 1u;
    } else {
        if (p < 0.0) {
            let r = 2.0 * sqrt(-p / 3.0);
            let val = clamp(-q / (2.0 * sqrt(-p * p * p / 27.0)), -1.0, 1.0);
            let theta = acos(val);
            let pi = 3.14159265359;
            (*roots)[0] = r * cos(theta / 3.0) - a / 3.0;
            (*roots)[1] = r * cos((theta + 2.0 * pi) / 3.0) - a / 3.0;
            (*roots)[2] = r * cos((theta + 4.0 * pi) / 3.0) - a / 3.0;
            *root_count = 3u;
        } else {
            (*roots)[0] = -a / 3.0;
            *root_count = 1u;
        }
    }
}
