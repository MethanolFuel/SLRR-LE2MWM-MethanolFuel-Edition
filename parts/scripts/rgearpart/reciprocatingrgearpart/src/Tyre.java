package java.game.parts.rgearpart.reciprocatingrgearpart;
import java.render.osd.*;
import java.io.*;
import java.util.*;
import java.util.resource.*;
import java.game.cars.*;
import java.game.*;
import java.game.parts.*;
import java.game.parts.bodypart.*;
import java.game.parts.enginepart.*;
import java.game.parts.rgearpart.*;

	public class Tyre extends ReciprocatingRGearPart
	{
		final static int	tcSTREET = 0;
		final static int	tcSPORT = 1;
		final static int	tcSUPERSPORT = 2;
		final static int	tcDRAGRADIAL = 3;

		//this is not where you want to edit ._.
		float	friction = 1.0;	//static friction coeff.
		float	sliction = 1.0;	//ratio of dynamic/static
		float	frictn_x = 1.0;	//factor (unused now)
		float	radius   = 0.01;
		float	stiffness = 10.0;
		float	rollres  = 0.01;	//rolling resistance
		float	loadcap  = 5000.0;	//load capacity Ns, 1 lb = ~0.45 kg, 1kg = ~ 9.81N
		float	loadsmooth = 0.5;
		float	CPatchMaximumAngle = 0.300;
		
		float 	min_pressure=0.5;//do not set lower than 0.3
		float	max_pressure=3.5;

		float 	deg_to_rad=3.1415926535897932384626433832795/180.0;//degrees to radians
		float	inch_to_mm=25.4;//inches to millimetres
		float	kg_to_n=9.80665002864;//kilograms to newtons
		float	kg_to_lb=2.20462262;//kilograms to pounds
		
		// Sala: the following variables are used for scripted calculations //

		float	insider_friction = 1.0;
		float	insider_frictn_x = 1.0;
		float	insider_sliction = 1.0;
		float	wheel_radius;// inches
		float	profile_ratio;// between 0 and 1
		float	tyre_width;// millimetres
		float	profile_height;// millimetres
		float	min_radius;
		float	max_radius;
		float	slide_interval;
		float	cpatch_width;// millimetres
		float	stiffness_rate;

		float	min_rollres;
		float	optimal_inflation;
		float	inflation;
		float	par_8;
		float	par_15;
		float	par_16;
		float	over_inflation;
		float	under_inflation;
		float	profile_height_ratio;

		int		maxRimWidth_i;
		int		minRimWidth_i;
		float	maxRimWidth;
		float	minRimWidth;
		float	RimWidth;

		float	default_fric=1.0;
		float	fric_mul=1.4;
		float	fric_x_mul=1.0;
		float	load_mul2=2.0;
		float	CPatchMaxA=16.000;
		float	CPatchMinA= 4.000;
		float	roll_res_mul = 0.725;
		float	roll_res_mul2 = 0.5;
		float	slic_mul=0.6;
		float	rubber_rigidity=1.000;
		float	par_15_mul=4;

		float	load_mul=1.5;
		float	gravity=9.81;
		float 	CPatch_static_width=0;
		float 	AirPressure=2.5;
		float 	massonwheel=300;
		
		int[]	render_types = new int[22];
		String	tyre_size_label;
		String 	compound_label;
		
		TyreCalculations Calc = new TyreCalculations();
		
		public Tyre()
		{
			repair_max_wear = 0.0;
			prestige_calc_weight = 15.0;

			catalog_view_ypr = new Ypr( 1.571, -0.7, 0.0 );
		}

		public void SetInflation( float I )
		{
			SetInflation( I, 0 );
		}

		public void SetInflation( float I, int forced )
		{
			if (inflation != I || forced) 
			{
				inflation = clampTo(I,min_pressure,max_pressure);
				over_inflation = clampTo(optimal_inflation/inflation,0,1);
				under_inflation = clampTo(inflation/optimal_inflation,0,1);

				loadcap = 30000.0; // tyre: max load capacity (Ns = kgs*gravity)
				CPatchMaximumAngle = deg_to_rad * (CPatchMinA+(0.6+(405.0-tyre_width)/405.0*0.4)*profile_ratio*(CPatchMaxA-CPatchMinA));
				radius = max_radius; // min_radius+(max_radius-min_radius)/misinflation;
				rollres = min_rollres/Calc.sqrt(inflation/2,2)*roll_res_mul2;//*(1.0-(tyre_width-215.0)/1000.0);		// tyre: rolling resistance - hungarian note: function of inflation
				par_16=500;
				par_15=par_16*par_15_mul;
				insider_frictn_x = 1.5*Math.sqrt(rubber_rigidity)*fric_x_mul;
				
				setMaxWear((0.15+(rubber_rigidity*rubber_rigidity*rubber_rigidity*rubber_rigidity)*0.85)*50000000000.0);
						
				WheelRef w = getWheel();
			
				if (w)
				{
					int whlID = getWheelID();
					massonwheel=Calc.getWeightOnWheel(whlID);
					Calc.setVars(tyre_width, profile_ratio*100, RimWidth , inflation, massonwheel);
					cpatch_width=Calc.getContactPatchWidth();
					
					stiffness = 20+Calc.getStiffnessRate()/5;
					insider_friction = friction * (0.5+0.5*(Calc.getContactPatchWidth()/250.0)) * fric_mul;
					insider_sliction = sliction * (0.5+0.5*(Calc.getContactPatchWidth()/250.0));
					profile_height_ratio = Calc.getTyreHeight()/profile_height;
					
					w.setPacejka(  0, 0.0 );//doesn't do anything because there's w.setSliction function
					w.setPacejka(  1, 0.0 );//prevents tyres from sliding,loosing traction(very low value,0.00001, haven't tested yet)
					w.setPacejka(  2, 0.0 );//setFriction
					w.setPacejka(  3, 0.0 );//+
					w.setPacejka(  4, 0.0 );//setStiffness
					w.setPacejka(  5, 0.0 );//shit -
					w.setPacejka(  6, 0.0 );//shit -
					w.setPacejka(  7, 0.0 );//bouncy,gripy stuff -
					w.setPacejka(  8, -2.0 );//par_8
					w.setPacejka(  9, 0.0 );//looks like Pacejka 10, bouncing, glitchy
					w.setPacejka( 10, 0.0 );//does something with suspension, goes up and down while standing still
					w.setPacejka( 11, 0.0 );//setMaxLoad
					w.setPacejka( 12, 1.0 );//not sure if this does something
					w.setPacejka( 13, 0.0 );//doesn't do anything, some function overrides it.
					w.setPacejka( 14, 0.0 );//doesn't do anything, some function overrides it.
					w.setPacejka( 15, par_15 );//looks like load stiffness, adds understeer, should be tuned along with Pacejka16
					w.setPacejka( 16, par_16 );//oversteering, lower value -> more oversteering(1000)

					w.setFrictn_x( insider_frictn_x );
					w.setLoadSmooth( loadsmooth*(-1.0) );
					
					w.setMaxLoad( loadcap );
					w.setStiffness( stiffness );//stiffness
					w.setFriction( insider_friction );
					w.setSliction( insider_sliction );
					w.setRadius( (Calc.getTyreHeight()+wheel_radius)/1000.0 );
					w.setWidth( Calc.getTyreWidth()/1000.0 );
					w.setRollRes( rollres );
					w.setBearing( 20.0 );//guolis
					//w.setCPatch(tyre_width/1000.0, CPatchMaximumAngle, 0);		this is in Wheel.java

					if ((whlID>=0) && ((whlID % 2)==1))//odd numbers, right side
						setSlotPos(1, new Vector3(0.0,0.0,0.0), new Ypr(0.0,0.0,0.0) );
					else//even numbers, left side
						setSlotPos(1, new Vector3(0.0,0.0,0.0), new Ypr(3.1415926535897932384626433832795,0.0,0.0) );
				}
			}
		}

		public void SetupRubber( int RT )
		{

			
			rubber_rigidity	=    1.000;
			load_mul	=    0.750*load_mul2;
			min_rollres	=    0.0055*roll_res_mul;		// tyre: depends on contact patch
			friction	=    default_fric/rubber_rigidity;	// tyre: static friction (compound efficiency)
			sliction	=    0.695*slic_mul;		// tyre: slip ratio (sliction*friction = slipfriction)
			loadsmooth	=    2.500;
			compound_label	= " tyre";
			police_check_fine_value = 0.0;
			
			if (RT == tcSPORT) // sport tyre //
			{
				rubber_rigidity	=    0.900;
				load_mul	=    0.800*load_mul2;
				min_rollres	=    0.005*roll_res_mul;		// tyre: depends on contact patch
				friction	=    default_fric/rubber_rigidity;	// tyre: static friction (compound efficiency)
				sliction	=    0.740*slic_mul;		// tyre: slip ratio (sliction*friction = slipfriction)
				loadsmooth	=    2.750;
				compound_label	= " medium compound sport tyre";
			} else
			if (RT == tcSUPERSPORT) // supersport tyre //
			{
				rubber_rigidity	=    0.750;
				load_mul	=    0.800*load_mul2;
				min_rollres	=    0.004*roll_res_mul;		// tyre: depends on contact patch
				friction	=    default_fric/rubber_rigidity;	// tyre: static friction (compound efficiency)
				sliction	=    0.760*slic_mul;		// tyre: slip ratio (sliction*friction = slipfriction)
				loadsmooth	=    3.000;
				compound_label	= " soft compound racing tyre";
				police_check_fine_value = 50.0;
			} else
			if (RT == tcDRAGRADIAL) // drag-radial //
			{
				rubber_rigidity	=    0.550;
				load_mul	=    1.800*load_mul2;
				min_rollres	=    0.002*roll_res_mul;		// tyre: depends on contact patch
				friction	=    default_fric/rubber_rigidity;	// tyre: static friction (compound efficiency)
				sliction	=    0.720*slic_mul;		// tyre: slip ratio (sliction*friction = slipfriction)
				loadsmooth	=    2.000;
				compound_label	= " drag radial";
				police_check_fine_value = 50.0;
			}
			else
			if (RT == tcSTREET) // street tyre //
			{
				rubber_rigidity	=    1.000;
				load_mul	=    0.750*load_mul2;
				min_rollres	=    0.0055*roll_res_mul;		// tyre: depends on contact patch
				friction	=    default_fric/rubber_rigidity;	// tyre: static friction (compound efficiency)
				sliction	=    0.695*slic_mul;		// tyre: slip ratio (sliction*friction = slipfriction)
				loadsmooth	=    2.500;
				compound_label	= " hard compound street tyre";
			}
			friction = (1.3+(friction-1.0)*0.35)*1.15;
			value /= rubber_rigidity;
			brand_new_prestige_value = 25.0/rubber_rigidity + tyre_width/10.0-20.0;
			if(name=="<unnamed>")//doesn't change tyres name if there's one
			name = tyre_size_label+compound_label;// removed name_prefix
		}

		public void calcStuffs()
		{
			for (minRimWidth_i=0; minRimWidth_i<21; minRimWidth_i++)
				if (render_types[minRimWidth_i])
				{
					minRimWidth = minRimWidth_i*0.5+5.0;
					break;
				}

			for (maxRimWidth_i=20; maxRimWidth_i>=0; maxRimWidth_i--)
				if (render_types[maxRimWidth_i])
				{
					maxRimWidth = maxRimWidth_i*0.5+5.0;
					break;
				}
				
			float R = wheel_radius*2.0/25.4;
			if(name=="<unnamed>")//doesn't change tyres name if there's one
				name = tyre_size_label+compound_label;// removed name_prefix
			
			if(maxRimWidth==minRimWidth)//a bit description fix, not much
			description = "This "+name+" is a "+Float.toString(tyre_width, "%1.0f mm wide ")+ compound_label +", diameter is "+Float.toString(R, "%1.0f inch at the rims and the height of sidewalls is ")+Float.toString(profile_height, "%1.0f mm. ")+Float.toString(R, "It is compatible with all %1.0f inch rims ")+Float.toString(minRimWidth, "that are %1.1f inch width.");
			else
			description = "This "+name+" is a "+Float.toString(tyre_width, "%1.0f mm wide ")+ compound_label +", diameter is "+Float.toString(R, "%1.0f inch at the rims and the height of sidewalls is ")+Float.toString(profile_height, "%1.0f mm. ")+Float.toString(R, "It is compatible with all %1.0f inch rims ")+Float.toString(minRimWidth, "that are between %1.1f and ")+Float.toString(maxRimWidth, "%1.1f inch width.");

			if (police_check_fine_value > 0.0)
				description = description + Float.toString(police_check_fine_value, " This tyre is not street legal! Police may fine you up to $%1.0f!");
		}

		public String installCheck( Part p, int[] slotId )
		{
			if (p)
			{
				if (p instanceof Wheel)
				{
					Wheel wheel = p;
					int wr1=wheel.wheel_radius*1000.0;
					int wr2=wheel_radius;
					if (wr1 != wr2)
						return "The tyre inner radius does not match the rim!";
					else
					if (wheel.rim_width > maxRimWidth)
						return "This tyre is too narrow for this rim!";
					else
					if (wheel.rim_width < minRimWidth)
						return "This tyre is too wide for this rim!";

					return null;
				}
				else
					return "Tyres can only be installed on rims!";
			}
			return "There's nothing to install this part to!";
		}

		public void SetupTyre( float W, float r, float R, float rW, int compound_type )
		{
			SetupTyre( W, r, R, rW, compound_type, -1.0, 104 );
		}
		
		public void SetupTyre( float W, float r, float R, float rW, int compound_type, float optI )
		{
			SetupTyre( W, r, R, rW, compound_type, optI, 104 );
		}

		public void SetupTyre( float W, float r, float R, float rW, int compound_type, float optI, int loadIndex )
		{
			value = HUF2USD(W*80+2500*(R-13.0));
			
			//optimal_inflation=2.0;
			
			wheel_radius	= R*25.4/2.0;
			tyre_width	= W;
			cpatch_width = W;
			profile_ratio	= r/100.0;
			RimWidth = rW;
			if (optI >= min_pressure && optI <= max_pressure)
				optimal_inflation = optI;
			
			profile_height	= tyre_width*profile_ratio;
			max_radius      = (wheel_radius+profile_height)/1000.0; // Sala: mm -> m //
			min_radius      = wheel_radius/1000.0; // Sala: 1.5cm more than rim diameter, mm -> m //
			if (max_radius < min_radius)
				min_radius = max_radius-0.01;

			SetupRubber(compound_type);
			if (optI >= min_pressure && optI <= max_pressure)
				SetInflation(optimal_inflation);
		}

		public void updatevariables()
		{
			SetInflation(inflation,1);
		}
		
		public void load( File saveGame )
		{
			super.load( saveGame );

			int	save_ver = saveGame.readInt();

			if (save_ver >= 1)
			{
				inflation = saveGame.readFloat();
			}
		}

		public void save( File saveGame )
		{
			super.save( saveGame );

			int	save_ver = 1;

			saveGame.write( save_ver );
			if (save_ver >= 1)
			{
				saveGame.write( inflation );
			}
		}

		//---------tuning
		public int isTuneable()
		{
			return 1;
		}

		// backup values //
		float	old_inflation;	
		float	new_inflation;

		final static SfxRef  inc_inflation_SFX = new SfxRef( MW_Mod:0x0071r );
		final static SfxRef  dec_inflation_SFX = new SfxRef( MW_Mod:0x0072r );
		
		public void buildTuningMenu( Menu m )
		{
			old_inflation = inflation;
			new_inflation = inflation;
			float f=Calc.getTyreHeight();
			m.addItem( "Tire pressure",		1, inflation, min_pressure, max_pressure, (max_pressure-min_pressure)*10+1, null ).changeVLabelText( Float.toString(new_inflation, "%1.2f bar")+Float.toString(new_inflation*14.7, " (%1.2f psi)"));//1psi = 0.069bar
			m.addItem( "Reset", 0 );
			getCar_LocalVersion();
		}

		public void endTuningSession( int cancelled )
		{
			if( cancelled )
			{
				SetInflation(old_inflation,1);
			}
			else
			{
				SetInflation(new_inflation,1);
				
				float spendTime = 1 + (new_inflation >  old_inflation) ? (new_inflation-old_inflation)*2*60 : 1;
				GameLogic.spendTime( spendTime );
			}
			//GameLogic.garage.osd.text.elementAt(1).changeText( "Debug: P " + inflation + "bar, TH " + Calc.getTyreHeight() + "mm, MoW " + massonwheel + "kg, Stf " + stiffness +", StfR " + stiffness_rate + ", CPW " + cpatch_width + " LoadCap:" + loadcap +"Ns" + " silction:" + insider_sliction + " friction:" + insider_friction + " under " + under_inflation + " over " + over_inflation + " fric x " + insider_frictn_x );
			
			getCar_LocalVersion();
			if (the_car)
			{
				the_car.command( "wakeup" );
			}
		}

		public void handleMessage( Event m )
		{
			if( m.cmd == 1 )
			{
				float sliderVal = ((Slider)m.gadget).value;

				if( sliderVal != new_inflation )
				{
					if(  sliderVal > new_inflation )
						inc_inflation_SFX.play( getPos(), 0.0, (sliderVal/max_pressure)+0.5, 0.5, 0, 1 ); // SFX
					else
						dec_inflation_SFX.play( getPos(), 0.0, (sliderVal/max_pressure)+0.5, 0.5, 0, 1 ); // SFX

					new_inflation = sliderVal;
					((Slider)m.gadget).changeVLabelText( Float.toString(new_inflation, "%1.2f bar")+Float.toString(new_inflation*14.7, " (%1.2f psi)"));
					
					if( the_car )
					{
						System.timeWarp( 1.0f );
						SetInflation(new_inflation,1);
						the_car.command( "wakeup" );
					}
				}
			}
			if( m.cmd == 0 )
			{
				float sliderVal = m.gadget.osd.findGadget( this, 1 ).value;
				new_inflation = optimal_inflation;
				
				if( sliderVal != new_inflation )
				{
					if( sliderVal < new_inflation )
						inc_inflation_SFX.play( getPos(), 0.0, 1, 0.5, 0, 1 ); // SFX
					else
						dec_inflation_SFX.play( getPos(), 0.0, 1, 0.5, 0, 1 ); // SFX
					
					m.gadget.osd.findGadget( this, 1 ).setValue( optimal_inflation );
					m.gadget.osd.findGadget( this, 1 ).changeVLabelText( Float.toString(optimal_inflation, "%1.2f bar")+Float.toString(optimal_inflation*14.7, " (%1.2f psi)"));
					
					if( the_car )
					{
						System.timeWarp( 1.0f );
						SetInflation(new_inflation,1);
						the_car.command( "wakeup" );
					}		
				}
			}
		}	
	}