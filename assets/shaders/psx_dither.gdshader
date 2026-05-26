shader_type canvas_item;

// Bayer 4x4 dithering overlay — adds PSX-style quantization grain.
// Drawn as a semi-transparent dark pattern over the entire screen via PSXOverlay.cs.
// Adjust STRENGTH to taste (0.0 = off, 0.1 = subtle, 0.25 = heavy).
uniform float STRENGTH : hint_range(0.0, 0.5) = 0.07;

void fragment() {
	int x = int(FRAGCOORD.x) % 4;
	int y = int(FRAGCOORD.y) % 4;

	// Bayer 4×4 ordered dither matrix (values 0–15 normalised to 0–1)
	float b00 =  0.0/16.0; float b01 =  8.0/16.0; float b02 =  2.0/16.0; float b03 = 10.0/16.0;
	float b10 = 12.0/16.0; float b11 =  4.0/16.0; float b12 = 14.0/16.0; float b13 =  6.0/16.0;
	float b20 =  3.0/16.0; float b21 = 11.0/16.0; float b22 =  1.0/16.0; float b23 =  9.0/16.0;
	float b30 = 15.0/16.0; float b31 =  7.0/16.0; float b32 = 13.0/16.0; float b33 =  5.0/16.0;

	float threshold = 0.0;
	if      (y == 0) threshold = (x == 0) ? b00 : (x == 1) ? b01 : (x == 2) ? b02 : b03;
	else if (y == 1) threshold = (x == 0) ? b10 : (x == 1) ? b11 : (x == 2) ? b12 : b13;
	else if (y == 2) threshold = (x == 0) ? b20 : (x == 1) ? b21 : (x == 2) ? b22 : b23;
	else             threshold = (x == 0) ? b30 : (x == 1) ? b31 : (x == 2) ? b32 : b33;

	COLOR = vec4(0.0, 0.0, 0.0, threshold * STRENGTH);
}
