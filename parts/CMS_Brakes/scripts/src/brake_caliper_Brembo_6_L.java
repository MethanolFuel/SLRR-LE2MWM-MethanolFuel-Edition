package java.game.parts.CMS_Brakes;

import java.game.parts.rgearpart.reciprocatingrgearpart.brake.*;

public class brake_caliper_Brembo_6_L extends DiscBrake
{
	public brake_caliper_Brembo_6_L( int id )
	{
		super( id );
		name = "Brembo family M 6 Piston";
		description = "Paintable";
		brand_new_prestige_value = 42.49;

		value = tHUF2USD(35.579);
		friction_pad		= 1.1;
		number_of_calipers	= 3.0;
	}
}