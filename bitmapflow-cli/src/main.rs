use std::path::PathBuf;

use clap::Parser;
use image::RgbaImage;
use opencv::core::{Mat, Size2i, Vec2f, CV_32FC2, CV_8UC3};
use opencv::optflow::{self, InterpolationType, RLOFOpticalFlowParameter};
use opencv::prelude::*;
use rayon::prelude::*;

#[derive(Parser, Debug)]
#[command(name = "bitmapflow-cli", about = "Generate sprite inbetween frames using optical flow")]
struct Args {
    /// Input frame PNG files, in order
    #[arg(short, long, required = true, num_args = 2..)]
    frames: Vec<PathBuf>,

    /// Output directory
    #[arg(short, long, default_value = ".")]
    output_dir: PathBuf,

    /// Number of inbetween frames to generate per pair
    #[arg(short, long, default_value = "2")]
    inbetweens: usize,

    /// Flow multiplier (motion amount)
    #[arg(long, default_value = "1.0")]
    flow_multiplier: f32,

    /// Optical flow algorithm: "simple" or "rlof"
    #[arg(long, default_value = "simple")]
    algorithm: String,

    /// Output filename prefix
    #[arg(long, default_value = "ib")]
    prefix: String,

    /// SimpleFlow layers
    #[arg(long, default_value = "3")]
    sf_layers: usize,

    /// SimpleFlow averaging block size
    #[arg(long, default_value = "2")]
    sf_block_size: usize,

    /// SimpleFlow max flow
    #[arg(long, default_value = "4")]
    sf_max_flow: usize,
}

fn load_frame(path: &PathBuf) -> Result<RgbaImage, String> {
    image::open(path)
        .map_err(|e| format!("Failed to open {}: {}", path.display(), e))
        .map(|img| img.into_rgba8())
}

fn rgba_to_bgr_mat(frame: &RgbaImage) -> Result<Mat, opencv::Error> {
    let w = frame.width() as i32;
    let h = frame.height() as i32;
    let mut mat = unsafe { Mat::new_rows_cols(h, w, CV_8UC3)? };
    for y in 0..h {
        for x in 0..w {
            let pixel = frame.get_pixel(x as u32, y as u32);
            let [r, g, b, a] = pixel.0;
            let (bgr_b, bgr_g, bgr_r) = if a < 30 { (0, 0, 0) } else { (b, g, r) };
            let mat_pixel = mat.at_2d_mut::<opencv::core::Vec3b>(y, x)?;
            **mat_pixel = [bgr_b, bgr_g, bgr_r];
        }
    }
    Ok(mat)
}

fn apply_flow(source: &RgbaImage, flow: &Mat, multiplier: f32) -> RgbaImage {
    let w = source.width();
    let h = source.height();
    RgbaImage::from_fn(w, h, |x, y| {
        let flow_vec: Vec2f = *flow.at_2d(y as i32, x as i32).unwrap();
        let (fx, fy) = if flow_vec[0].is_finite() && flow_vec[1].is_finite() {
            (flow_vec[0] * multiplier, flow_vec[1] * multiplier)
        } else {
            (0.0, 0.0)
        };
        let src_x = (x as f32 - fx).round().max(0.0).min((w - 1) as f32) as u32;
        let src_y = (y as f32 - fy).round().max(0.0).min((h - 1) as f32) as u32;
        *source.get_pixel(src_x, src_y)
    })
}

fn compute_flow(mat_a: &Mat, mat_b: &Mat, use_rlof: bool, args: &Args) -> Result<Mat, opencv::Error> {
    let size = mat_a.size()?;
    let mut flow = unsafe { Mat::new_size(size, CV_32FC2)? };

    if use_rlof {
        let rlof_param = RLOFOpticalFlowParameter::create()?;
        let grid_step = Size2i::new(3, 3);
        optflow::calc_optical_flow_dense_rlof(
            mat_a, mat_b, &mut flow, rlof_param,
            0.0, grid_step, InterpolationType::INTERP_EPIC,
            128, 0.05, 100.0, 15, 100, true, 500.0, 1.5, false,
        )?;
    } else {
        optflow::calc_optical_flow_sf(
            mat_a, mat_b, &mut flow,
            args.sf_layers as i32,
            args.sf_block_size as i32,
            args.sf_max_flow as i32,
        )?;
    }
    Ok(flow)
}

/// Generate inbetween frames between two source images.
/// Returns `count` frames, each warping progressively from `a` toward `b`.
fn generate_ibs_between(
    frame_a: &RgbaImage,
    frame_b: &RgbaImage,
    count: usize,
    flow_multiplier: f32,
    use_rlof: bool,
    args: &Args,
) -> Result<Vec<RgbaImage>, String> {
    if count == 0 {
        return Ok(Vec::new());
    }

    let mat_a = rgba_to_bgr_mat(frame_a).map_err(|e| format!("mat_a: {e}"))?;
    let mat_b = rgba_to_bgr_mat(frame_b).map_err(|e| format!("mat_b: {e}"))?;
    let flow = compute_flow(&mat_a, &mat_b, use_rlof, args)
        .map_err(|e| format!("flow failed: {e}"))?;

    let mut frames = Vec::with_capacity(count);
    for i in 1..=count {
        let mult = i as f32 / (count + 1) as f32 * flow_multiplier;
        frames.push(apply_flow(frame_a, &flow, mult));
    }
    Ok(frames)
}

fn main() -> Result<(), String> {
    let args = Args::parse();
    std::fs::create_dir_all(&args.output_dir)
        .map_err(|e| format!("Failed to create output dir: {e}"))?;

    let use_rlof = args.algorithm == "rlof";
    let input: Vec<RgbaImage> = args.frames.iter().map(load_frame).collect::<Result<Vec<_>, _>>()?;
    let n = input.len();

    if n < 2 {
        return Err("Need at least 2 input frames".into());
    }

    let (w, h) = (input[0].width(), input[0].height());
    for (i, f) in input.iter().enumerate() {
        if f.width() != w || f.height() != h {
            return Err(format!("Frame {i} size mismatch: {}x{} vs {}x{}", f.width(), f.height(), w, h));
        }
    }

    // Generate inbetween frames for each consecutive pair
    // Total output: (n - 1) * inbetweens frames
    let pairs: Vec<(&RgbaImage, &RgbaImage)> = input.windows(2).map(|w| (&w[0], &w[1])).collect();
    let total_pairs = pairs.len();

    let all_ibs: Vec<RgbaImage> = pairs
        .par_iter()
        .enumerate()
        .flat_map(|(idx, (fa, fb))| {
            eprintln!("Inbetweening pair {}/{} ({}→{})", idx + 1, total_pairs, idx, idx + 1);
            generate_ibs_between(fa, fb, args.inbetweens, args.flow_multiplier, use_rlof, &args)
                .unwrap_or_else(|e| {
                    eprintln!("Error on pair {idx}: {e}");
                    Vec::new()
                })
        })
        .collect();

    let total = all_ibs.len();
    for (i, ib) in all_ibs.iter().enumerate() {
        let filename = format!("{}{:04}.png", args.prefix, i);
        let path = args.output_dir.join(&filename);
        ib.save(&path).map_err(|e| format!("Failed to save {filename}: {e}"))?;
    }

    eprintln!("Done — generated {total} inbetween frames in {}", args.output_dir.display());
    Ok(())
}
