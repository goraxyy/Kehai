# Scene map: SampleScene

Generated on 2026-11-06 by `tools/scene/scene_map.py` from `Assets/!_Project/_Core/Scenes/SampleScene.unity`. Don't edit by hand: rerun the script after changing the scene.

The scene and the art aren't in git (the repository is code and docs only), so this file is how the repository records **where everything is**. Karen's own map of the same store (regions, links, chokepoints) is [`STORE_MAP.md`](STORE_MAP.md); the building, products and stocking are in [`STORE.md`](STORE.md); room plans in [`LEVEL_DESIGN.md`](LEVEL_DESIGN.md) and [`INTERIOR_MODELS.md`](INTERIOR_MODELS.md).

- **Coordinates:** world metres, shown as (x, y, z). The street is north (high Z), the store runs south along −Z to the stockroom, and east is +X. y is height.
- **Size:** 5777 objects in the hierarchy, 972 of them prefab instances, 7 top-level objects.
- **How it's organised:** `Kehai → Scene → Organize by Area and Name Objects` (`SceneHierarchyOrganizer.cs`) puts everything under a few roots by area and gives objects stable names.

## Top level

| object | where | objects inside |
|---|---|---|
| **Models_Island** | (13.0, 0.0, -37.3) | 1464 |
| **Supermarket** | (0.0, 0.0, 0.0) | 3336 |
| **Exterior** | (0.0, 0.0, 0.0) | 792 |
| **Game_Systems** | (0.0, 0.0, 0.0) | 38 |
| **Authoring_References** | (0.0, 0.0, 0.0) | 115 |
| **Generated_Content** | (0.0, 0.0, 0.0) | 25 |
| **Control_Face_01** | (0.0, 0.0, -0.5) | 0 |

What each root holds:

- **Models_Island**: an off-map showcase grid, away from the store, with one of every model and product box, used to check models; nothing on it is part of the playable store.
- **Supermarket**: the building: one folder per room (sales floor, staff room, office, WC, lobby, storage, cold room).
- **Exterior**: outside: the storefront, the service yard (backstreet) and the parking lot.
- **Game_Systems**: the player, the UI, the light and post-processing volume, and customer routing points.
- **Authoring_References**: builder containers, inactive prototypes and legacy fixtures kept for reference.
- **Generated_Content**: objects the builders and the game generate (room reflection probes, floor and ceiling patches, the shelf product drawer).

## The hierarchy

Three levels down from each root. Long lists of similar objects are summarised by name.

- **Models_Island** at (13.0, 0.0, -37.3) — 1464 objects inside
  - **Restocking_Boxes_01** at (13.0, 0.0, -37.3) — 554 objects inside
    - **Pet_Food_01** at (24.3, 0.0, -26.7) — 37 objects inside
      - **Box_Pet_Cat_Tin_01** at (95.8, 0.4, 102.6) — 5 objects inside
      - **Sign_01** at (24.3, 0.0, 102.6)
      - **Box_Pet_Hamster_Bed_01** at (106.0, 0.4, 102.6) — 5 objects inside
      - **Box_Pet_Bird_Seed_01** at (89.0, 0.4, 102.6) — 5 objects inside
      - **Box_Pet_Dog_Can_01** at (99.2, 0.4, 102.6) — 5 objects inside
      - **Box_Pet_Cat_Pouch_01** at (92.4, 0.4, 102.6) — 5 objects inside
      - **Box_Pet_Dog_Kibble_01** at (102.6, 0.4, 102.6) — 5 objects inside
    - **Snacks_01** at (24.3, 0.0, -37.9) — 37 objects inside
      - **Box_Snack_Wasabiwave_01** at (106.0, 0.4, 72.5) — 5 objects inside
      - **Box_Snack_Nutzy_01** at (95.8, 0.4, 72.5) — 5 objects inside
      - **Sign_01** at (24.3, 0.0, 72.5)
      - **Box_Snack_Krunchos_01** at (89.0, 0.4, 72.5) — 5 objects inside
      - **Box_Snack_Pretzelpals_01** at (102.6, 0.4, 72.5) — 5 objects inside
      - **Box_Snack_Krunchos_Sco_01** at (92.4, 0.4, 72.5) — 5 objects inside
      - **Box_Snack_Popcorn_01** at (99.2, 0.4, 72.5) — 5 objects inside
    - **Confectionery_01** at (24.3, 0.0, -36.3) — 37 objects inside
      - **Box_Sweet_Kittokatsu_01** at (95.8, 0.4, 76.8) — 5 objects inside
      - **Box_Sweet_Mintz_01** at (99.2, 0.4, 76.8) — 5 objects inside
      - **Box_Sweet_Gummygang_01** at (92.4, 0.4, 76.8) — 5 objects inside
      - **Box_Sweet_Chocobo_01** at (89.0, 0.4, 76.8) — 5 objects inside
      - **Box_Sweet_Pokki_01** at (106.0, 0.4, 76.8) — 5 objects inside
      - **Box_Sweet_Mochibites_01** at (102.6, 0.4, 76.8) — 5 objects inside
      - **Sign_01** at (24.3, 0.0, 76.8)
    - **Canned_01** at (24.3, 0.0, -33.1) — 37 objects inside
      - **Box_Canned_Miso_01** at (92.4, 0.4, 85.4) — 5 objects inside
      - **Box_Canned_Beans_01** at (89.0, 0.4, 85.4) — 5 objects inside
      - **Box_Canned_Tuna_01** at (106.0, 0.4, 85.4) — 5 objects inside
      - **Box_Canned_Sweetcorn_01** at (102.6, 0.4, 85.4) — 5 objects inside
      - **Box_Canned_Nori_01** at (95.8, 0.4, 85.4) — 5 objects inside
      - **Sign_01** at (24.3, 0.0, 85.4)
      - **Box_Canned_Sardines_01** at (99.2, 0.4, 85.4) — 5 objects inside
    - **Produce_01** at (24.3, 0.0, -45.9) — 40 objects inside
      - **Box_Produce_Mikan_01** at (99.2, 0.4, 51.0) — 5 objects inside
      - **Box_Produce_Bananas_01** at (92.4, 0.4, 51.0) — 5 objects inside
      - **Box_Produce_Apples_01** at (89.0, 0.4, 51.0) — 5 objects inside
      - **Sign_01** at (24.3, 0.0, 51.0)
      - **Box_Produce_Daikon_01** at (95.8, 0.4, 51.0) — 5 objects inside
      - **Box_Produce_Tomatoes_01** at (106.0, 0.4, 51.0) — 5 objects inside
      - **Box_Produce_Saladmix_01** at (102.6, 0.3, 51.0) — 8 objects inside
    - *13 children:* Pet_Food, Snacks, Confectionery, Canned, Produce, Dairy, Bakery, Frozen, Personal_Care, Soft_Drinks, Household, Noodles, and 1 more kinds
  - **WC_01** at (0.0, 0.0, 0.0) — 79 objects inside
    - **Double_Vanity_01** at (59.0, 0.0, 51.0) — 5 objects inside
      - **Mirror_02** at (59.0, 1.7, 51.1)
      - **Mirror_01** at (59.0, 1.7, 50.9)
      - **Counter_01** at (59.0, 0.8, 51.0)
      - **Double_Base_01** at (59.0, 0.4, 51.0)
      - **Mirror_Spine_01** at (59.0, 1.6, 51.0)
    - **Cleaning_Hide_Cabinet_01** at (59.0, 0.0, 57.1) — 11 objects inside
      - **Side_01** at (58.3, 1.2, 57.1)
      - **Side_02** at (59.7, 1.2, 57.1)
      - **Hinge_Empty_Cupboard_01** at (58.3, 0.0, 56.4) — 5 objects inside
      - **Top_01** at (59.0, 2.5, 57.1)
      - **Back_01** at (59.0, 1.2, 57.7)
      - **Low_Threshold_01** at (59.0, 0.0, 57.1)
    - **Toilet_Cabin_01** at (47.0, 0.0, 51.0) — 12 objects inside
      - **Latch_Return_01** at (48.0, 1.4, 49.4)
      - **Back_01** at (47.0, 1.4, 52.7)
      - **Toilet_01** at (47.3, 0.0, 52.1) — 1 objects inside
      - **Hinge_Jamb_01** at (45.9, 1.4, 49.4)
      - **Sanitary_Bin_01** at (48.0, 0.0, 52.2) — 1 objects inside
      - **Hinge_Toilet_Cabin_01** at (46.1, 0.0, 49.4) — 1 objects inside
      - **Side_02** at (48.3, 1.4, 51.0)
      - **Side_01** at (45.7, 1.4, 51.0)
      - **Lintel_01** at (46.8, 2.6, 49.4)
    - **Toilet_Cabin_Open_Interior_01** at (59.0, 0.0, 63.0) — 12 objects inside
      - **Toilet_01** at (59.3, 0.0, 64.0) — 1 objects inside
      - **Hinge_Jamb_01** at (57.9, 1.4, 61.3)
      - **Back_01** at (59.0, 1.4, 64.7)
      - **Latch_Return_01** at (60.0, 1.4, 61.3)
      - **Hinge_Toilet_Cabin_01** at (58.1, 0.0, 61.3) — 1 objects inside
      - **Sanitary_Bin_01** at (60.0, 0.0, 64.2) — 1 objects inside
      - **Lintel_01** at (58.8, 2.6, 61.3)
      - **Side_01** at (57.7, 1.4, 63.0)
      - **Side_02** at (60.3, 1.4, 63.0)
    - **Hand_Dryer_01** at (65.0, 0.0, 51.0) — 1 objects inside
      - **Body_01** at (65.0, 0.2, 51.0)
    - *15 children:* Caution_Sign, Cleaning_Bucket, Double_Vanity, Vent_Grille, Cleaning_Supplies, Cleaning_Hide_Cabinet, Toilet_Cabin, Soap_Dispenser, Toilet_Cabin_Open_Interior, Hand_Dryer, Cleaning_Station, Drying_Station, and 3 more kinds
  - **Catalogue_Labels_01** at (0.0, 0.0, 0.0) — 163 objects inside
    - *163 children:* Gallery_Line ×20, Label ×11, Label_Shelf_Twoside_3_Grey_1_05_4_2_M ×2, Label_Shelf_Oneside_3_Grey_0_54_4_2_M ×2, Label_Kettle_0_37_0_23_0_33_M, Label_Pallet_Stack_1_2_1_2_0_48_M, Label_Toilet_0_66_1_17_1_03_M, Label_Service_Workbench_3_2_0_88_1_M, Label_Public_Bin_0_7_0_65_1_04_M, Label_Outdoor_Waste_Container_2_2_1_44_1_45_M, Label_Drying_Station_0_84_0_22_0_7_M, Label_Umbrella_Stand_0_65_0_45_0_9_M, and 120 more kinds
  - **Staff_Room_01** at (0.0, 0.0, 0.0) — 135 objects inside
    - **Coat_Rail_01** at (13.0, 0.0, 31.0) — 1 objects inside
      - **Backboard_01** at (13.0, 0.1, 31.0)
    - **Locker_Open_Interior_01** at (19.2, 0.0, 37.5) — 8 objects inside
      - **Cap_01** at (19.2, 0.0, 37.5)
      - **Side_01** at (18.6, 1.2, 37.5)
      - **Cap_02** at (19.2, 2.4, 37.5)
      - **Back_01** at (19.2, 1.2, 38.2)
      - **Hinge_Locker_01** at (18.6, 0.0, 36.8) — 1 objects inside
      - **Upper_Shelf_01** at (19.2, 2.3, 37.6)
      - **Side_02** at (19.7, 1.2, 37.5)
    - **Coffee_Machine_01** at (19.0, 0.0, 13.0) — 2 objects inside
      - **Drip_Tray_01** at (19.0, 0.0, 12.9)
      - **Body_01** at (19.0, 0.3, 13.1)
    - **Kitchen_Counter_01** at (31.0, 0.0, 7.0) — 3 objects inside
      - **Plinth_01** at (31.0, 0.1, 7.0)
      - **Cabinet_01** at (31.0, 0.5, 7.0)
      - **Worktop_01** at (31.0, 0.9, 7.0)
    - **First_Aid_01** at (37.0, 0.0, 25.0) — 1 objects inside
      - **Cabinet_01** at (37.0, 0.3, 25.0)
    - *33 children:* Coat_Rail, Locker_Open_Interior, Fire_Extinguisher, Coffee_Machine, Kitchen_Counter, First_Aid, Small_Plant, Sink_Counter, Wall_Clock, Wall_Shelf, Changing_Cabin, Whiteboard, and 21 more kinds
  - **Products_01** at (13.0, 0.0, -37.3) — 186 objects inside
    - **Dairy_01** at (18.0, 0.0, -29.7) — 13 objects inside
      - *13 children:* Tag_Dairy_Milk_Whole, Tag_Dairy_Yoghurt, Tag_Dairy_Creamcheese, Tag_Dairy_Butter, Tag_Dairy_Milk_Skim, Tag_Dairy_Eggs, Sign, Dairy_Milk_Skim, Dairy_Creamcheese, Dairy_Milk_Whole, Dairy_Eggs, Dairy_Butter, and 1 more kinds
    - **Cereal_01** at (18.0, 0.0, -27.5) — 15 objects inside
      - *15 children:* Tag_Cereal_Kafe, Tag_Cereal_Sencha, Tag_Cereal_Branflakies, Sign, Tag_Cereal_Chocoloops, Tag_Cereal_Granola, Tag_Cereal_Oatsy, Tag_Cereal_Honeynutz, Cereal_Granola, Cereal_Oatsy, Cereal_Branflakies, Cereal_Honeynutz, and 3 more kinds
    - **Pet_Food_01** at (18.0, 0.0, -18.7) — 13 objects inside
      - *13 children:* Sign, Tag_Pet_Bird_Seed, Tag_Pet_Cat_Tin, Tag_Pet_Hamster_Bed, Tag_Pet_Cat_Pouch, Tag_Pet_Dog_Can, Tag_Pet_Dog_Kibble, Pet_Dog_Kibble, Pet_Dog_Can, Pet_Hamster_Bed, Pet_Cat_Pouch, Pet_Bird_Seed, and 1 more kinds
    - **Canned_01** at (18.0, 0.0, -23.1) — 13 objects inside
      - *13 children:* Tag_Canned_Miso, Tag_Canned_Beans, Tag_Canned_Sardines, Tag_Canned_Sweetcorn, Sign, Tag_Canned_Nori, Tag_Canned_Tuna, Canned_Sardines, Canned_Miso, Canned_Tuna, Canned_Beans, Canned_Sweetcorn, and 1 more kinds
    - **Confectionery_01** at (18.0, 0.0, -25.3) — 13 objects inside
      - *13 children:* Tag_Sweet_Chocobo, Tag_Sweet_Gummygang, Tag_Sweet_Pokki, Tag_Sweet_Mintz, Tag_Sweet_Kittokatsu, Tag_Sweet_Mochibites, Sign, Sweet_Pokki, Sweet_Chocobo, Sweet_Kittokatsu, Sweet_Mintz, Sweet_Mochibites, and 1 more kinds
    - *13 children:* Dairy, Cereal, Pet_Food, Canned, Confectionery, Frozen, Household, Noodles, Soft_Drinks, Personal_Care, Snacks, Produce, and 1 more kinds
  - **Lobby_01** at (0.0, 0.0, 0.0) — 57 objects inside
    - **Umbrella_Stand_01** at (47.0, 0.0, 79.0) — 3 objects inside
      - **End_02** at (47.3, 0.3, 79.0)
      - **End_01** at (46.7, 0.3, 79.0)
      - **Drip_Tray_01** at (47.0, 0.0, 79.0)
    - **Snacks_Vending_01** at (59.0, 0.0, 73.0) — 1 objects inside
      - **Case_01** at (59.0, 1.1, 73.0)
    - **Lobby_Bench_01** at (47.0, 0.0, 73.0) — 11 objects inside
      - *11 children:* Seat_Slat ×5, Back_Slat ×4, Leg ×2
    - **Trolley_Corral_01** at (71.0, -0.0, 79.0) — 4 objects inside
      - **Rail_Collision_01** at (69.6, 0.5, 79.0)
      - **Rail_Collision_02** at (71.0, 0.5, 79.0)
      - **Rail_Collision_03** at (72.4, 0.5, 79.0)
      - **Rear_Rail_01** at (71.0, 0.8, 80.6)
    - **Service_Counter_01** at (65.0, 0.0, 73.2) — 4 objects inside
      - **Return_Extension_01** at (62.9, 0.4, 72.5)
      - **Top_01** at (65.0, 1.1, 73.2)
      - **Lower_Counter_01** at (62.9, 0.8, 72.5)
      - **Base_01** at (65.0, 0.5, 73.2)
    - *17 children:* Entry_Mat, Directory, Umbrella_Stand, Snacks_Vending, Lobby_Bench, Trolley_Corral, Service_Counter, Basket_Stack, Drinks_Vending, Bag_Lockers, Lobby_Planter, Queue_Post, and 5 more kinds
  - **Retail_Fixtures_01** at (0.0, 0.0, 0.0) — 132 objects inside
    - **Shelf_Pillar_Endcap_Tall_01** at (74.8, 0.0, 4.5) — 9 objects inside
      - **Retail_Shelf_Details_01** `ShelfDetail_Shelfpillar_E_3_grey.prefab` at (74.8, 0.0, 4.5)
      - **Shelf_Price_Rail_01** at (76.7, 1.4, 7.0)
      - **Shelf_Price_Rail_02** at (76.7, 0.8, 7.0)
      - **Wall1_02** *(inactive)* at (77.3, 1.0, 7.0)
      - **Shelf_Deck_01** at (77.0, 0.8, 7.0)
      - **Shelf_Price_Rail_03** at (76.7, 0.2, 7.0)
      - **Shelf_Deck_03** at (77.0, 1.4, 7.0)
      - **Shelf_Deck_02** at (77.0, 0.2, 7.0)
      - **Wall1_01** at (77.3, 1.0, 7.0)
    - **Shelf_Pillar_T_Tall_01** at (57.0, 0.0, 10.5) — 9 objects inside
      - **Retail_Shelf_Details_01** `ShelfDetail_Shelfpillar_T_3_grey.prefab` at (57.0, 0.0, 10.5)
      - **Wall1_01** at (59.0, 1.0, 13.0)
      - **Shelf_Deck_03** at (59.0, 1.4, 13.0)
      - **Shelf_Price_Rail_03** at (59.5, 0.2, 13.0)
      - **Shelf_Price_Rail_01** at (59.5, 1.4, 13.0)
      - **Shelf_Price_Rail_02** at (59.5, 0.8, 13.0)
      - **Shelf_Deck_02** at (59.0, 0.8, 13.0)
      - **Wall2_01** at (58.7, 1.0, 13.0)
      - **Shelf_Deck_01** at (59.0, 0.2, 13.0)
    - **Shelf_Pillar_S_Tall_01** at (51.2, 0.0, 16.7) — 5 objects inside
      - **Retail_Shelf_Details_01** `ShelfDetail_Shelfpillar_s_3_grey.prefab` at (51.2, 0.0, 16.7)
      - **Shelf_Deck_01** at (53.0, 0.2, 19.0)
      - **Shelf_Deck_02** at (53.0, 0.8, 19.0)
      - **Shelf_Deck_03** at (53.0, 1.4, 19.0)
      - **Wall1_01** at (53.2, 1.0, 19.0)
    - **Shelf_Pillar_B_Tall_01** at (62.8, 0.0, 10.5) — 7 objects inside
      - **Solid_Lower_Plinth_01** at (65.0, 0.4, 13.0)
      - **Retail_Shelf_Details_01** `ShelfDetail_Shelfpillar_b_3_grey.prefab` at (62.8, 0.0, 10.5)
      - **Shelf_Price_Rail_02** at (64.7, 1.4, 13.0)
      - **Shelf_Price_Rail_01** at (64.7, 0.8, 13.0)
      - **Shelf_Deck_02** at (65.0, 1.4, 13.0)
      - **Shelf_Deck_01** at (65.0, 0.8, 13.0)
      - **Wall1_01** at (65.3, 1.0, 13.0)
    - **Shelf_Pillar_Transition_T_Tall_01** at (75.2, 0.0, 10.5) — 6 objects inside
      - **Retail_Shelf_Details_01** `ShelfDetail_Shelfpillar_onetwoT_3_grey.prefab` at (75.2, 0.0, 10.5)
      - **Shelf_Deck_01** at (77.0, 0.2, 13.0)
      - **Shelf_Deck_02** at (77.0, 0.8, 13.0)
      - **Shelf_Deck_03** at (77.0, 1.4, 13.0)
      - **Wall2_01** at (77.0, 1.0, 13.0)
      - **Wall1_01** at (77.2, 1.0, 13.0)
    - *14 children:* Shelf_Double_Sided_Tall ×2, Shelf_Single_Sided_Tall ×2, Shelf_Pillar_Endcap_Tall, Shelf_Pillar_T_Tall, Shelf_Pillar_S_Tall, Shelf_Pillar_B_Tall, Shelf_Pillar_Transition_T_Tall, Shelf_Pillar_L_Tall, Shelf_Pillar_Transition_L_Tall, Shelf_Pillar_R_Tall, Shelf_Pillar_+_Tall, Shelf_Pillar_I_Tall
  - **Office_01** at (0.0, 0.0, 0.0) — 49 objects inside
    - **Credenza_01** at (13.0, 0.0, 57.0) — 2 objects inside
      - **Oak_Top_01** at (13.0, 0.8, 57.0)
      - **Case_01** at (13.0, 0.4, 57.0)
    - **Printer_Cabinet_01** at (19.0, 0.0, 57.0) — 2 objects inside
      - **Case_01** at (19.0, 0.4, 57.0)
      - **Top_01** at (19.0, 0.8, 57.0)
    - **Security_Console_01** at (19.0, 0.0, 51.0) — 6 objects inside
      - **Recorder_01** at (20.3, 0.3, 51.1)
      - **Oak_Worktop_01** at (19.0, 0.8, 51.0)
      - **Foot_01** at (17.4, 0.0, 51.0)
      - **Steel_Side_01** at (17.4, 0.4, 51.0)
      - **Foot_02** at (20.6, 0.0, 51.0)
      - **Steel_Side_02** at (20.6, 0.4, 51.0)
    - **Office_Safe_01** at (7.0, 0.0, 57.1) — 1 objects inside
      - **Steel_Case_01** at (7.0, 0.7, 57.1)
    - **Tall_Cabinet_01** at (37.0, 0.0, 51.0) — 1 objects inside
      - **Case_01** at (37.0, 1.1, 51.0)
    - *19 children:* Credenza, Printer_Cabinet, Laptop, Desk_Lamp, Security_Console, Office_Safe, Tall_Cabinet, Keyboard, Printer, Water_Dispenser, Binders, Briefing_Table, and 7 more kinds
  - **Exterior_And_Tools_01** at (0.0, 0.0, 0.0) — 99 objects inside
    - **Cold_Hiding_Cabinet_01** at (37.0, 0.0, 99.1) — 11 objects inside
      - **Base_01** at (37.0, 0.0, 99.1)
      - **Right_Insulated_Wall_01** at (37.9, 1.3, 99.1)
      - **Left_Insulated_Wall_01** at (36.1, 1.3, 99.1)
      - **Top_01** at (37.0, 2.5, 99.1)
      - **Hinge_Cold_Storage_Cabinet_01** at (36.2, 0.0, 98.4) — 5 objects inside
      - **Insulated_Back_01** at (37.0, 1.3, 99.7)
    - **Public_Bin_01** at (13.0, 0.0, 93.0) — 1 objects inside
      - **Body_01** at (13.0, 0.5, 93.0)
    - **Outdoor_Waste_Container_01** at (43.0, 0.0, 99.0) — 5 objects inside
      - **Side_02** at (44.0, 0.8, 99.0)
      - **End_02** at (43.0, 0.8, 99.7)
      - **Bottom_01** at (43.0, 0.2, 99.0)
      - **End_01** at (43.0, 0.8, 98.4)
      - **Side_01** at (42.0, 0.8, 99.0)
    - **Service_Workbench_01** at (7.0, 0.0, 99.0) — 4 objects inside
      - **Top_01** at (7.0, 0.9, 99.0)
      - **Leg_01** at (7.4, 0.5, 99.0)
      - **Leg_02** at (8.4, 0.5, 99.0)
      - **Case_01** at (6.1, 0.5, 99.0)
    - **Service_Cage_01** at (37.0, 0.0, 93.0) — 1 objects inside
      - **Floor_01** at (37.0, 0.1, 93.0)
    - *24 children:* Cold_Hiding_Cabinet, Public_Bin, Outdoor_Waste_Container, Service_Workbench, Bollard, Service_Cage, Wall_Lamp, Storage_Chiller, Flashlight, Recycling_Bin, Bike_Rack, Poster_Frame, and 12 more kinds
  - **Catalogue_Floor_01** at (60.0, -0.0, 60.0)
- **Supermarket** at (0.0, 0.0, 0.0) — 3336 objects inside
  - **Cold_Room** at (0.0, 0.0, 0.0) — 271 objects inside
    - **Refrigeration** at (0.0, 0.0, 0.0) — 169 objects inside
      - **Cold_Storage_Cabinets_01** at (0.0, 0.0, 0.0) — 168 objects inside
    - **Doors** at (0.0, 0.0, 0.0) — 1 objects inside
      - **Partition_Door_Header_01** at (55.0, 5.2, -182.5)
    - **Architecture** at (0.0, 0.0, 0.0) — 38 objects inside
      - *38 children:* Panel_Rib ×18, Cream_Wall_Panel ×9, Charcoal_Plinth ×9, Storage_Cold_Room_Partition ×2
    - **Utilities** at (0.0, 0.0, 0.0) — 19 objects inside
      - *19 children:* Drain_Grille ×12, Evaporator_Service_Riser ×3, Cold_Room_Evaporator ×3, Cold_Room_Drain
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 2 objects inside
      - **COLD_STORAGE_KEEP_CLOSED_01** at (0.0, 0.0, -175.1)
      - **CHILLED_FROZEN_01** at (0.0, 0.0, -173.9)
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 36 objects inside
      - **White_Frame_Door_01** at (55.0, 0.0, -183.5) — 3 objects inside
      - *33 children:* Finished_Wall_Wall ×32, White_Frame_Door
  - **Lobby** at (0.0, 0.0, 0.0) — 202 objects inside
    - **Architecture** at (0.0, 0.0, 0.0) — 17 objects inside
      - *17 children:* Lobby_Wall_Lining ×6, Panel_Rib ×4, Charcoal_Plinth ×2, Cream_Wall_Panel ×2, Lobby_Floor, Lobby_East_Lining, Lobby_Lowered_Ceiling
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 40 objects inside
      - *40 children:* Parked_Trolley ×16, Leg ×5, Upright ×4, Basket_Pickup ×3, Trolley_Bay ×2, Lower_Counter, Foot, Store_Directory, Tray, Lobby_First_Aid, Snacks_Vending, Back, and 3 more kinds
    - **Doors** at (0.0, 0.0, 0.0) — 14 objects inside
      - **Sign_ENTRANCE_01** at (67.5, 2.9, -119.1) — 5 objects inside
      - **Door_Lining_Header_03** at (42.5, 3.1, -110.0)
      - **Outer_Door_Mat_01** `EntryMat.prefab` at (42.5, 0.0, -111.2)
      - **Door_Lining_Header_02** at (67.5, 3.1, -120.0)
      - **Inner_Door_Mat_02** `EntryMat.prefab` at (67.5, 0.0, -118.8)
      - **Outer_Door_Mat_02** `EntryMat.prefab` at (67.5, 0.0, -111.2)
      - **Door_Lining_Header_04** at (67.5, 3.1, -110.0)
      - **Inner_Door_Mat_01** `EntryMat.prefab` at (42.5, 0.0, -118.8)
      - **Door_Lining_Header_01** at (42.5, 3.1, -120.0)
    - **Inactive_Originals** at (0.0, 0.0, 0.0) — 8 objects inside
      - **Lobby_West_North_Lining_01** *(inactive)* at (25.1, 1.8, -112.9)
      - **Entry_Recycling_01** `RecyclingStation.prefab` *(inactive)* at (71.5, 0.0, -119.7) — 1 objects inside
      - **Vending_Recycling_01** `RecyclingStation.prefab` *(inactive)* at (51.0, 0.0, -119.7) — 1 objects inside
      - **Lobby_West_South_Lining_01** *(inactive)* at (25.1, 1.8, -118.8)
      - **Service_Recycling_01** `RecyclingStation.prefab` *(inactive)* at (35.5, 0.0, -110.3) — 1 objects inside
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Finished_Wall_Wall_03** at (85.0, 2.0, -117.5)
      - **Finished_Wall_Wall_02** at (85.0, 6.0, -112.5)
      - **Finished_Wall_Wall_01** at (85.0, 2.0, -112.5)
      - **Finished_Wall_Wall_04** at (85.0, 6.0, -117.5)
    - **Cabinets_And_Storage** at (0.0, 0.0, 0.0) — 2 objects inside
      - **Shelf_03** `ParcelShelf.prefab` at (31.0, 0.0, -110.4)
      - **Case_01** `BagLockers.prefab` at (35.0, 0.0, -119.6)
    - **Recycling** at (0.0, 0.0, 0.0) — 39 objects inside
      - **Lobby_Furnished_Recycling_01** at (35.5, 0.0, -110.3) — 12 objects inside
      - **Lobby_Furnished_Recycling_02** at (51.0, 0.0, -119.7) — 12 objects inside
      - **Lobby_Furnished_Recycling_03** at (71.5, 0.0, -119.7) — 12 objects inside
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 64 objects inside
      - **Sign_THANK_YOU_01** at (42.5, 2.9, -110.9) — 5 objects inside
      - **Sign_TROLLEYS_01** at (81.3, 2.9, -115.0) — 5 objects inside
      - **Sign_WELCOME_TO_KEHAI_01** at (55.0, 3.0, -115.0) — 5 objects inside
      - **Sign_WELCOME_01** at (67.5, 2.9, -110.9) — 5 objects inside
      - **Sign_WC_01** at (26.7, 2.9, -116.7) — 5 objects inside
      - *24 children:* Label ×12, Label_KEHAI_STORE_GUIDE_WC_CUSTOMER_SERVICE_GROCERIES_DRINKS_CHILLED_CHE, Sign_THANK_YOU, Sign_TROLLEYS, Sign_WELCOME_TO_KEHAI, Sign_WELCOME, Sign_WC, Label_SNACKS, Community_Noticeboard, Sign_CUSTOMER_SERVICE, Sign_BASKETS, Label_DRINKS, and 1 more kinds
    - **Greenery** at (0.0, 0.0, 0.0) — 5 objects inside
      - **South_Waiting_Greenery_01** `LobbyPlanter.prefab` at (62.0, 0.0, -119.6)
      - **Waiting_Area_Greenery_01** `LobbyPlanter.prefab` at (51.9, 0.0, -110.4)
      - **Basket_Area_Greenery_01** `LobbyPlanter.prefab` at (76.0, 0.0, -110.4)
      - **WC_Approach_Planter_01** `LobbyPlanter.prefab` at (28.5, 0.0, -119.6)
      - **Waiting_Area_Greenery_02** `LobbyPlanter.prefab` at (58.7, 0.0, -110.4)
  - **WC** at (0.0, 0.0, 0.0) — 97 objects inside
    - **Inactive_Originals** at (0.0, 0.0, 0.0) — 1 objects inside
      - **East_North_Lining_01** *(inactive)* at (25.0, 1.8, -112.5)
    - **Architecture** at (0.0, 0.0, 0.0) — 15 objects inside
      - *15 children:* Panel_Rib ×5, Charcoal_Plinth ×3, Cream_Wall_Panel ×3, North_Lining, West_Lining, WC_Floor, WC_Lowered_Ceiling
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 1 objects inside
      - **Label_WC_01** at (0.0, 0.0, -116.7)
    - **Utilities** at (0.0, 0.0, 0.0) — 1 objects inside
      - **WC_Extract_West_01** `VentGrille.prefab` at (10.2, 3.0, -116.6)
    - **Cabinets_And_Storage** at (0.0, 0.0, 0.0) — 20 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (22.1, 0.0, -121.6) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (16.8, 0.0, -121.6) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (19.4, 0.0, -111.8) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (14.1, 0.0, -111.8) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (11.5, 0.0, -111.8) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (14.1, 0.0, -121.6) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (16.8, 0.0, -111.8) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (19.4, 0.0, -121.6) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (22.1, 0.0, -111.8) — 1 objects inside
      - **Lintel_01** `ToiletCabin.prefab` at (11.5, 0.0, -121.6) — 1 objects inside
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 44 objects inside
      - **Mirror_Spine_01** `DoubleVanity.prefab` at (17.4, 0.0, -116.7)
      - **WC_Finishing_01** at (0.0, 0.0, 0.0) — 42 objects inside
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 8 objects inside
      - **Finished_Wall_Wall_02** *(inactive)* at (25.0, 6.0, -112.5)
      - **Finished_Wall_Wall_01** *(inactive)* at (25.0, 2.0, -112.5)
      - **Finished_Wall_Wall_05** at (10.0, 2.0, -122.5)
      - **Finished_Wall_Wall_08** at (10.0, 6.0, -117.5)
      - **Finished_Wall_Wall_03** at (10.0, 2.0, -112.5)
      - **Finished_Wall_Wall_07** at (10.0, 2.0, -117.5)
      - **Finished_Wall_Wall_06** at (10.0, 6.0, -122.5)
      - **Finished_Wall_Wall_04** at (10.0, 6.0, -112.5)
  - **Shared_Building** at (0.0, 0.0, 0.0) — 1584 objects inside
    - **Lighting_And_Audio** at (0.0, 0.0, 0.0) — 306 objects inside
      - **Mall_Speakers_01** at (0.0, 0.0, 0.0) — 64 objects inside
      - **Ceiling_Light_Sources_01** at (0.0, 0.0, 0.0) — 240 objects inside
    - **Wall_Finishes** at (0.0, 0.0, 0.0) — 7 objects inside
      - **Shared_Room_Wall_02** at (25.0, 5.3, -142.5)
      - **Shared_Room_Wall_05** at (25.0, 4.0, -123.3)
      - **Shared_Room_Wall_06** at (25.0, 5.3, -116.7)
      - **Shared_Room_Wall_04** at (25.0, 5.3, -130.0)
      - **Shared_Room_Wall_07** at (25.0, 4.0, -112.9)
      - **Shared_Room_Wall_01** at (25.0, 4.0, -146.7)
      - **Shared_Room_Wall_03** at (25.0, 4.0, -136.2)
    - **Doors** at (0.0, 0.0, 0.0) — 80 objects inside
      - **Automatic_Doors_01** at (20.0, 0.0, -124.9) — 52 objects inside
      - **Hinged_Doors_01** at (20.0, 0.0, -124.9) — 26 objects inside
    - **Lighting** at (0.0, 0.0, 0.0) — 961 objects inside
      - **Ceiling_Lamp_Models_01** at (0.0, 0.0, 0.0) — 960 objects inside
    - **Original_Walls** at (0.0, 0.0, 0.0) — 213 objects inside
      - **Walls_01** at (25.0, 2.0, -172.5) — 212 objects inside
    - **Original_Surfaces** at (0.0, 0.0, 0.0) — 6 objects inside
      - **Original_Floor_01** at (20.0, 0.0, -124.9)
      - **Original_Roof_01** at (47.5, 8.3, -150.0) — 4 objects inside
    - **Rooftop_Equipment** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Housing_01** `HVAC.prefab` at (34.0, 8.6, -164.0)
      - **Housing_01** `HVAC.prefab` at (34.0, 8.6, -139.0)
      - **Housing_01** `HVAC.prefab` at (64.0, 8.6, -139.0)
      - **Housing_01** `HVAC.prefab` at (64.0, 8.6, -164.0)
  - **Storage** at (0.0, 0.0, 0.0) — 155 objects inside
    - **Shelving** at (0.0, 0.0, 0.0) — 30 objects inside
      - **Storage_Rack_01** at (12.7, 0.0, -175.7) — 29 objects inside
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 53 objects inside
      - *53 children:* Finished_Wall_Wall ×53
    - **Boxes_And_Props** at (0.0, 0.0, 0.0) — 12 objects inside
      - **Cardboards_01** at (35.6, 0.3, -184.2) — 11 objects inside
    - **Architecture** at (0.0, 0.0, 0.0) — 56 objects inside
      - *56 children:* Panel_Rib ×24, Charcoal_Plinth ×12, Cream_Wall_Panel ×12, Wall_Beside_Open_Service_Access ×4, Upper_Cream_Cladding, Parapet, Red_Brand_Fascia, Parapet_Cap
  - **Office** at (0.0, 0.0, 0.0) — 107 objects inside
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Finished_Wall_Wall_03** at (10.0, 2.0, -127.5)
      - **Finished_Wall_Wall_01** at (10.0, 2.0, -132.5)
      - **Finished_Wall_Wall_02** at (10.0, 6.0, -132.5)
      - **Finished_Wall_Wall_04** at (10.0, 6.0, -127.5)
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Office_Notices_01** `Noticeboard.prefab` at (16.3, 1.3, -136.6)
      - **Office_Clock_01** `WallClock.prefab` at (17.2, 2.6, -123.4)
      - **Label_OFFICE_01** at (0.0, 0.0, -130.0)
      - **Planning_Whiteboard_01** `Whiteboard.prefab` at (10.2, 1.2, -133.2)
    - **Lighting** at (0.0, 0.0, 0.0) — 2 objects inside
      - **Desk_Task_Lamp_01** `DeskLamp.prefab` at (10.5, 0.8, -123.7)
      - **Briefing_Pendant_01** `CeilingPendant.prefab` at (17.1, 2.8, -130.2)
    - **Recycling** at (0.0, 0.0, 0.0) — 15 objects inside
      - **Office_Furnished_Recycling_01** at (24.6, 0.0, -127.3) — 12 objects inside
      - **Personnel_Binders_01** `Binders.prefab` at (15.6, 0.9, -136.3)
      - **Filing_Reference_Binders_01** `Binders.prefab` at (10.3, 1.4, -130.1)
    - **Inactive_Originals** at (0.0, 0.0, 0.0) — 9 objects inside
      - **Bracket_01** `WallScreen.prefab` *(inactive)* at (24.8, 1.6, -133.2)
      - **East_Wall_01** *(inactive)* at (25.0, 4.0, -132.9)
      - **East_Wall_03** *(inactive)* at (25.0, 4.0, -115.4)
      - **Door_Header_01** *(inactive)* at (25.0, 5.3, -130.0)
      - **East_South_Lining_01** *(inactive)* at (24.9, 4.0, -135.8)
      - **Office_Paper_Recycling_01** `RecyclingStation.prefab` *(inactive)* at (24.6, 0.0, -127.3) — 1 objects inside
      - **East_Wall_02** *(inactive)* at (25.0, 4.0, -123.3)
      - **Door_Header_02** *(inactive)* at (25.0, 5.3, -116.7)
    - **Doors** at (0.0, 0.0, 0.0) — 2 objects inside
      - **Header_01** `RoomDoor.prefab` at (25.0, 0.0, -130.0)
      - **Header_01** `RoomDoor.prefab` at (25.0, 0.0, -116.7)
    - **Greenery** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Office_Lounge_Plant_01** `SmallPlant.prefab` at (21.8, 0.4, -133.6)
      - **Office_South_Plant_01** `TallPlant.prefab` at (20.7, 0.0, -136.2)
      - **Briefing_Table_Plant_01** `SmallPlant.prefab` at (18.6, 0.8, -130.2)
      - **Office_Northeast_Plant_01** `TallPlant.prefab` at (24.5, 0.0, -123.8)
    - **Cabinets_And_Storage** at (0.0, 0.0, 0.0) — 5 objects inside
      - **Archive_Cabinet_01** `TallCabinet.prefab` at (20.6, 0.0, -123.7)
      - **Case_01** `FilingCabinet.prefab` at (10.5, 0.0, -130.9)
      - **Case_01** `FilingCabinet.prefab` at (10.5, 0.0, -130.1)
      - **Case_01** `FilingCabinet.prefab` at (10.5, 0.0, -129.2)
      - **Archive_Cabinet_02** `TallCabinet.prefab` at (21.8, 0.0, -123.7)
    - **Architecture** at (0.0, 0.0, 0.0) — 18 objects inside
      - *18 children:* Panel_Rib ×6, Wall_Beside_Open_Service_Access ×4, Cream_Wall_Panel ×2, Charcoal_Plinth ×2, Office_Floor, West_Lining, Office_Lowered_Ceiling, Office_WC_Partition
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 34 objects inside
      - *34 children:* Briefing_Chair_South ×4, Briefing_Chair_North ×4, Back ×3, Foot ×3, Oak_Top ×2, Visitor_Chair ×2, Manager_Keyboard, Backboard, Office_Water_Dispenser, Office_Camera, Meeting_Papers, In_Tray, and 10 more kinds
  - **Sales_Floor** at (0.0, 0.0, 0.0) — 753 objects inside
    - **Signage** at (0.0, 0.0, 0.0) — 261 objects inside
      - **Aisle_Signs_01** at (0.0, 0.0, 0.0) — 260 objects inside
    - **Refrigeration** at (0.0, 0.0, 0.0) — 79 objects inside
      - **Retail_Chillers_01** at (0.0, 0.0, 0.0) — 78 objects inside
    - **Shelving** at (0.0, 0.0, 0.0) — 215 objects inside
      - **Maze_Shelves_01** at (82.0, 0.0, -162.5) — 214 objects inside
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 66 objects inside
      - *54 children:* Finished_Wall_Wall ×50, White_Automatic_Portal ×2, White_Frame_Door ×2
    - **Checkouts** at (0.0, 0.0, 0.0) — 23 objects inside
      - **Checkout_Impulse_Shelf_01** `Checkout.prefab` at (29.5, 0.0, -132.5)
      - **Counter_Facing_01** at (44.8, 0.9, -132.5)
      - **Checkout_Collision_Models_01** at (53.5, 0.0, -153.0) — 18 objects inside
      - **Checkout_Impulse_Shelf_01** `Checkout.prefab` at (39.5, 0.0, -132.5)
      - **Checkout_Impulse_Shelf_01** `Checkout.prefab` at (34.5, 0.0, -132.5)
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 6 objects inside
      - **Sign_CHECKOUT_03** at (39.9, 2.8, -130.5) — 1 objects inside
      - **Sign_CHECKOUT_02** at (34.9, 2.8, -130.5) — 1 objects inside
      - **Sign_CHECKOUT_01** at (29.9, 2.8, -130.5) — 1 objects inside
    - **Doors** at (0.0, 0.0, 0.0) — 3 objects inside
      - **Sales_Entrance_Greenery_02** `LobbyPlanter.prefab` at (51.0, 0.0, -120.4)
      - **Sales_Entrance_Greenery_01** `LobbyPlanter.prefab` at (28.0, 0.0, -120.4)
      - **Sales_Entrance_Greenery_03** `LobbyPlanter.prefab` at (75.0, 0.0, -120.4)
    - **Architecture** at (0.0, 0.0, 0.0) — 92 objects inside
      - *56 children:* Panel_Rib ×22, Charcoal_Plinth ×11, Cream_Wall_Panel ×11, Upright ×4, Sales_Floor_Bin ×3, Sales_Floor, Parapet_Cap, Red_Brand_Fascia, Upper_Cream_Cladding, Parapet
  - **Staff_Room** at (0.0, 0.0, 0.0) — 159 objects inside
    - **Gameplay_Tools** at (0.0, 0.0, 0.0) — 25 objects inside
      - **Mop_01** at (11.2, 1.0, -149.7) — 3 objects inside
      - **Staff_Radio_01** at (10.3, 1.7, -146.7) — 2 objects inside
      - **Time_Clock_01** at (24.9, 1.2, -139.3) — 2 objects inside
      - **Flashlight_01** at (15.5, 1.0, -146.4) — 4 objects inside
      - **Tool_Snap_Points_01** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Coffee_Machine_01** at (10.6, 1.0, -148.0) — 3 objects inside
      - **Stock_Test_Crate_01** at (12.6, 0.5, -149.4)
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 28 objects inside
      - *28 children:* Cup ×4, Dining_Chair_North ×4, Dining_Chair_South ×4, Oak_Top ×3, Foot ×2, Bracket, Clean_Uniforms, Appliance_Counter, Fire_Extinguisher, Kettle, Staff_Fridge, Backboard, and 4 more kinds
    - **Inactive_Originals** at (0.0, 0.0, 0.0) — 11 objects inside
      - **East_Lining_02** *(inactive)* at (24.9, 4.0, -146.7)
      - **East_Lining_01** *(inactive)* at (24.9, 4.0, -139.1)
      - **Entrance_Infill_02** *(inactive)* at (24.9, 1.2, -141.6)
      - **Parapet_01** *(inactive)* at (9.9, 8.3, -150.0)
      - **East_Doorway_Top_01** *(inactive)* at (24.9, 5.2, -142.5)
      - **Entrance_Infill_01** *(inactive)* at (24.9, 1.2, -143.4)
      - **Parapet_Cap_01** *(inactive)* at (9.9, 9.0, -150.0)
      - **Kitchen_Recycling_01** `RecyclingStation.prefab` *(inactive)* at (14.8, 0.0, -149.7) — 1 objects inside
      - **Red_Brand_Fascia_01** *(inactive)* at (9.9, 7.3, -150.0)
      - **Upper_Cream_Cladding_01** *(inactive)* at (9.9, 5.5, -150.0)
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 16 objects inside
      - **White_Frame_Door_01** at (18.5, 0.0, -175.0) — 3 objects inside
      - *13 children:* Finished_Wall_Wall ×12, White_Frame_Door
    - **Greenery** at (0.0, 0.0, 0.0) — 3 objects inside
      - **Lounge_Corner_Plant_01** `TallPlant.prefab` at (24.5, 0.0, -149.5)
      - **Lounge_Table_Plant_01** `SmallPlant.prefab` at (22.0, 0.4, -147.5)
      - **Table_Plant_01** `SmallPlant.prefab` at (16.8, 0.8, -142.5)
    - *11 children:* Gameplay_Tools, Furniture_And_Props, Inactive_Originals, Doors_And_Wall_Finishes, Greenery, Cabinets_And_Storage, Signs_And_Notices, Recycling, Architecture, Doors, Lighting
- **Exterior** at (0.0, 0.0, 0.0) — 792 objects inside
  - **Storefront** at (0.0, 0.0, 0.0) — 188 objects inside
    - **Greenery** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Front_Planter_03** `LobbyPlanter.prefab` at (61.0, 0.0, -109.5)
      - **Front_Planter_02** `LobbyPlanter.prefab` at (48.0, 0.0, -109.5)
      - **Front_Planter_04** `LobbyPlanter.prefab` at (80.0, 0.0, -109.5)
      - **Front_Planter_01** `LobbyPlanter.prefab` at (28.0, 0.0, -109.5)
    - **Lighting** at (0.0, 0.0, 0.0) — 8 objects inside
      - **Canopy_Light_07** at (70.0, 4.0, -106.5)
      - **Canopy_Light_04** at (52.0, 4.0, -106.5)
      - **Canopy_Light_05** at (58.0, 4.0, -106.5)
      - **Canopy_Light_02** at (40.0, 4.0, -106.5)
      - **Canopy_Light_01** at (34.0, 4.0, -106.5)
      - **Canopy_Light_06** at (64.0, 4.0, -106.5)
      - **Canopy_Light_03** at (46.0, 4.0, -106.5)
      - **Canopy_Light_08** at (76.0, 4.0, -106.5)
    - **Recycling** at (0.0, 0.0, 0.0) — 26 objects inside
      - **Front_Bin_West_01** at (35.0, 0.0, -109.5) — 12 objects inside
      - **Front_Bin_East_01** at (73.7, 0.0, -109.5) — 12 objects inside
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 7 objects inside
      - **Canopy_Underside_01** at (55.0, 4.0, -107.2)
      - **Canopy_Column_03** at (77.0, 2.0, -104.6)
      - **Front_Bike_Rack_01** `BikeRack.prefab` at (16.5, 0.0, -107.5)
      - **Canopy_Column_01** at (33.0, 2.0, -104.6)
      - **Leg_01** `LobbyBench.prefab` at (76.8, 0.0, -109.4)
      - **Canopy_Column_02** at (55.0, 2.0, -104.6)
      - **Leg_01** `LobbyBench.prefab` at (30.9, 0.0, -109.4)
    - **Doors** at (0.0, 0.0, 0.0) — 9 objects inside
      - **Portal_Protection_04** `Bollard.prefab` at (71.0, 0.0, -104.8)
      - **Portal_Protection_01** `Bollard.prefab` at (39.0, 0.0, -104.8)
      - **Sign_ENTRANCE_01** at (67.5, 3.2, -109.7) — 1 objects inside
      - **Portal_Header_02** at (67.5, 2.6, -109.7)
      - **Portal_Protection_02** `Bollard.prefab` at (46.0, 0.0, -104.8)
      - **Portal_Header_01** at (42.5, 2.6, -109.7)
      - **Entrance_Canopy_01** at (55.0, 4.2, -107.2)
      - **Portal_Protection_03** `Bollard.prefab` at (64.0, 0.0, -104.8)
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 42 objects inside
      - *36 children:* Finished_Wall_Wall ×34, White_Automatic_Portal ×2
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 14 objects inside
      - **Sign_EXIT_01** at (42.5, 3.2, -109.7) — 1 objects inside
      - **Sign_EVERYDAY_MARKET_01** at (55.0, 4.7, -109.7) — 1 objects inside
      - **Sign_KEHAI_01** at (55.0, 5.9, -109.7) — 1 objects inside
      - **Sign_PIPISI_COLD_DRINKS_01** at (51.2, 1.6, -109.6) — 1 objects inside
      - **Sign_OPEN_24H_01** at (82.7, 4.6, -109.6) — 1 objects inside
      - **Sign_RAMYUM_WARM_UP_01** at (58.8, 1.6, -109.6) — 1 objects inside
      - **Sign_KRUNCHOS_SNACK_BREAK_01** at (55.0, 1.6, -109.6) — 1 objects inside
    - **Architecture** at (0.0, 0.0, 0.0) — 70 objects inside
      - *70 children:* Panel_Rib ×30, Cream_Wall_Panel ×19, Charcoal_Plinth ×17, Upper_Cream_Cladding, Parapet, Red_Brand_Fascia, Parapet_Cap
  - **Parking** at (0.0, 0.0, 0.0) — 522 objects inside
    - **Roads_And_Furniture** at (0.0, 0.0, 0.0) — 227 objects inside
      - *137 children:* Traffic_Arrow ×22, Transfer_Hatching ×18, Public_Road_Centre_Dash ×16, Approach_Centre_Dash ×16, Crossing_Stripe ×14, North_Aisle_Centre_Dash ×8, Pedestrian_Protection ×8, Forecourt_Centre_Dash ×8, EV_Charge_Point ×4, Parking_Recycling ×2, Protected_Entrance_Walkway ×2, Give_Way ×2, and 13 more kinds
    - **Greenery** at (0.0, 0.0, 0.0) — 25 objects inside
      - *25 children:* Planter_Kerb ×14, Parking_Tree ×9, West_Continuous_Planting, Central_Planting
    - **Lighting** at (0.0, 0.0, 0.0) — 36 objects inside
      - *18 children:* Base ×18
    - **Bays_And_Markings** at (0.0, 0.0, 0.0) — 222 objects inside
      - *222 children:* Wheel_Stop ×53, Accessible_Bay_Mark ×4, EV_Bay_Mark ×4, Family_Bay_Mark ×2, EV_Bay_3_Lower_Line, Standard_Bay_44_Upper_Line, Standard_Bay_35_Upper_Line, Accessible_Bay_50_Back_Line, Standard_Bay_25_Lower_Line, EV_Bay_2_Upper_Line, Standard_Bay_43_Back_Line, Standard_Bay_27_Back_Line, and 151 more kinds
    - **Vehicles** at (0.0, 0.0, 0.0) — 7 objects inside
      - **Body_01** `ParkedCar.prefab` at (47.5, 0.0, -70.3)
      - **Body_01** `ParkedCar.prefab` at (47.5, 0.0, -92.7)
      - **Body_01** `ParkedCar.prefab` at (31.5, 0.0, -89.5)
      - **Body_01** `ParkedCar.prefab` at (72.5, 0.0, -70.3)
      - **Body_01** `ParkedCar.prefab` at (61.5, 0.0, -70.3)
      - **Body_01** `ParkedCar.prefab` at (17.5, 0.0, -79.9)
      - **Body_01** `ParkedCar.prefab` at (86.5, 0.0, -73.5)
  - **Service_Yard** at (0.0, 0.0, 0.0) — 79 objects inside
    - **Furniture_And_Props** at (0.0, 0.0, 0.0) — 10 objects inside
      - **Keep_Clear_Lane_Marking_02** at (21.9, 0.0, -162.5)
      - **Bale_02** `CardboardBales.prefab` at (10.6, 0.0, -167.0)
      - **Sorted_Empty_Pallets_02** `PalletStack.prefab` at (10.7, 0.0, -161.7)
      - **Keep_Clear_Lane_Marking_01** at (13.4, 0.0, -162.5)
      - **SERVICE_YARD_STAFF_ONLY_01** at (0.0, 0.0, -150.1)
      - **Empty_Returns_Cage_01** `ServiceCage.prefab` at (10.7, 0.0, -171.2)
      - **SORT_BAGS_BEFORE_DISPOSAL_01** at (0.0, 0.0, -164.2)
      - **Sorted_Empty_Pallets_03** `PalletStack.prefab` at (10.7, 0.0, -163.0)
      - **Sorted_Empty_Pallets_01** `PalletStack.prefab` at (10.7, 0.0, -160.4)
      - **Returns_Roll_Cage_Bay_01** `ServiceCage.prefab` at (10.7, 0.0, -157.0)
    - **Signs_And_Notices** at (0.0, 0.0, 0.0) — 4 objects inside
      - **Sign_KEHAI_SERVICE_01** at (47.5, 5.8, -190.3) — 1 objects inside
      - **Sign_KEHAI_01** at (85.3, 5.7, -148.0) — 1 objects inside
    - **Utilities** at (0.0, 0.0, 0.0) — 19 objects inside
      - **Backstreet_01** at (39.3, 0.0, -109.1) — 9 objects inside
      - **East_Downpipe_03** `Downpipe.prefab` at (85.3, 0.0, -170.0)
      - **Yard_Drain_01** at (17.5, 0.0, -162.5)
      - **West_Downpipe_01** `Downpipe.prefab` at (9.7, 0.0, -115.0)
      - **East_Downpipe_02** `Downpipe.prefab` at (85.3, 0.0, -140.0)
      - **West_Downpipe_03** `Downpipe.prefab` at (9.7, 0.0, -170.0)
      - **East_Downpipe_04** `Downpipe.prefab` at (85.3, 0.0, -185.0)
      - **East_Downpipe_01** `Downpipe.prefab` at (85.3, 0.0, -115.0)
      - **West_Downpipe_02** `Downpipe.prefab` at (9.7, 0.0, -140.0)
      - **West_Downpipe_04** `Downpipe.prefab` at (9.7, 0.0, -185.0)
    - **Doors_And_Wall_Finishes** at (0.0, 0.0, 0.0) — 10 objects inside
      - **Finished_Wall_Wall_03** at (25.0, 2.0, -152.5)
      - **Finished_Wall_Wall_10** at (25.0, 6.0, -172.5)
      - **Finished_Wall_Wall_04** at (25.0, 6.0, -152.5)
      - **Finished_Wall_Wall_02** at (25.0, 6.0, -162.5)
      - **Finished_Wall_Wall_06** at (25.0, 6.0, -167.5)
      - **Finished_Wall_Wall_01** at (25.0, 2.0, -162.5)
      - **Finished_Wall_Wall_05** at (25.0, 2.0, -167.5)
      - **Finished_Wall_Wall_08** at (25.0, 6.0, -157.5)
      - **Finished_Wall_Wall_07** at (25.0, 2.0, -157.5)
      - **Finished_Wall_Wall_09** at (25.0, 2.0, -172.5)
    - **Recycling** at (0.0, 0.0, 0.0) — 12 objects inside
      - **Bottom_01** `OutdoorWasteContainer.prefab` at (23.9, 0.0, -169.0) — 3 objects inside
      - **Bottom_01** `OutdoorWasteContainer.prefab` at (23.9, 0.0, -163.0) — 3 objects inside
      - **Bottom_01** `OutdoorWasteContainer.prefab` at (23.9, 0.0, -157.0) — 3 objects inside
    - **Architecture** at (0.0, 0.0, 0.0) — 11 objects inside
      - *11 children:* Panel_Rib ×10, Backstreet_Floor
    - **Lighting** at (0.0, 0.0, 0.0) — 6 objects inside
      - **Weatherproof_Service_Light_01** `WallLamp.prefab` at (24.8, 3.4, -154.0)
      - **Service_Yard_Light_01** at (23.9, 3.4, -154.0)
      - **Weatherproof_Service_Light_02** `WallLamp.prefab` at (24.8, 3.4, -161.0)
      - **Service_Yard_Light_03** at (23.9, 3.4, -169.0)
      - **Service_Yard_Light_02** at (23.9, 3.4, -161.0)
      - **Weatherproof_Service_Light_03** `WallLamp.prefab` at (24.8, 3.4, -169.0)
- **Game_Systems** at (0.0, 0.0, 0.0) — 38 objects inside
  - **Directional_Light_01** at (3.4, 14.1, -5.9)
  - **UI** at (0.0, 0.0, 0.0) — 20 objects inside
    - **HUD_Crosshair_And_Tasks_01** at (0.0, 0.0, 0.0) — 3 objects inside
      - **Crosshair_01** at (0.0, 0.0, 0.0)
      - **Task_Panel_01** at (0.0, 0.0, 0.0) — 1 objects inside
    - **HUD_Inventory_And_Burnout_01** at (0.0, 0.0, 0.0) — 15 objects inside
      - **Inventory_Bar_01** at (0.0, 0.0, 0.0) — 12 objects inside
      - **Burnout_Bar_01** at (0.0, 0.0, 0.0) — 1 objects inside
  - **Global_Volume_01** at (0.0, 0.0, 0.0)
  - **Game_Systems_01** at (2.1, 0.4, 3.3)
  - **Scene_Name_Index** at (0.0, 0.0, 0.0)
  - **Event_System_01** at (0.0, 0.0, 0.0)
  - **Customer_Routing** at (0.0, 0.0, 0.0) — 4 objects inside
    - **Cashier_Point_01** at (41.5, 0.0, -132.5)
    - **Exit_Point_01** at (42.5, 0.0, -110.0)
    - **Entrance_Point_01** at (67.5, 0.0, -110.0)
    - **Customer_Spawner_01** at (67.5, 0.0, -102.5)
  - **Player** at (64.9, 1.1, -105.3) — 6 objects inside
    - **Stash_Point_01** at (64.9, 1.1, -105.3)
    - **Camera_Root_01** at (64.9, 1.1, -105.3) — 4 objects inside
      - **Tool_Hold_Point_01** at (64.9, 1.1, -105.3)
      - **Main_Camera_01** at (64.9, 1.6, -105.3) — 2 objects inside
- **Authoring_References** at (0.0, 0.0, 0.0) — 115 objects inside
  - **Builder_Containers** at (0.0, 0.0, 0.0) — 10 objects inside
    - **WC_Furnished_01** at (0.0, 0.0, 0.0)
    - **Environment_Fit_Corrections_01** at (0.0, 0.0, 0.0)
    - **Layout_15x15_01** at (20.0, 0.0, -124.9)
    - **Store_Finished_01** at (0.0, 0.0, 0.0)
    - **Staff_Room_Furnished_01** at (0.0, 0.0, 0.0)
    - **Office_Furnished_01** at (0.0, 0.0, 0.0)
    - **Cold_Chain_Retail_And_Storage_01** at (0.0, 0.0, 0.0)
    - **Parking_Finished_01** at (0.0, 0.0, 0.0)
    - **Environment_Refined_01** at (0.0, 0.0, 0.0)
    - **Lobby_Furnished_01** at (0.0, 0.0, 0.0)
  - **Inactive_Prototypes** at (0.0, 0.0, 0.0) — 2 objects inside
    - **Trashcan_02** `Trashcan.prefab` *(inactive)* at (38.7, 0.0, -124.3)
    - **Trashcan_01** `Trashcan.prefab` *(inactive)* at (38.9, 0.0, -127.0)
  - **Legacy_Fixtures** at (0.0, 0.0, 0.0) — 100 objects inside
    - **Legacy_Refrigeration_Source_01** at (54.5, 0.0, -180.1) — 99 objects inside
      - *99 children:* Legacy_Fridge_Type ×99
- **Generated_Content** at (0.0, 0.0, 0.0) — 25 objects inside
  - **Room_Reflections_01** at (0.0, 0.0, 0.0) — 7 objects inside
    - **Reflections_Staff_Room_02** at (17.8, 4.0, -129.9)
    - **Reflections_Cold_Storage_01** at (69.8, 4.0, -182.2)
    - **Reflections_Lobby_01** at (54.8, 4.0, -115.1)
    - **Reflections_Stockroom_01** at (33.0, 4.0, -182.2)
    - **Reflections_Sales_Floor_01** at (54.8, 4.0, -147.9)
    - **Reflections_Staff_Room_03** at (17.8, 4.0, -116.9)
    - **Reflections_Staff_Room_01** at (17.8, 4.0, -143.7)
  - **Shelf_Product_Drawer_01** at (0.0, 0.0, 0.0)
  - **Room_Patches_01** at (0.0, 0.0, 0.0) — 15 objects inside
    - *15 children:* Floor ×8, Ceiling ×7
- **Control_Face_01** at (0.0, 0.0, -0.5)

## Gameplay objects and fixtures

The time clock, tools, doors, coolers, bins, spawn points and the like (first of each kind).

| path | where | prefab |
|---|---|---|
| Game_Systems / Customer_Routing / Cashier_Point_01 | (41.5, 0.0, -132.5) |  |
| Game_Systems / Customer_Routing / Customer_Spawner_01 | (67.5, 0.0, -102.5) |  |
| Game_Systems / Customer_Routing / Entrance_Point_01 | (67.5, 0.0, -110.0) |  |
| Game_Systems / Customer_Routing / Exit_Point_01 | (42.5, 0.0, -110.0) |  |
| Game_Systems / Global_Volume_01 | (0.0, 0.0, 0.0) |  |
| Game_Systems / Player | (64.9, 1.1, -105.3) |  |
| Game_Systems / Player / Camera_Root_01 | (64.9, 1.1, -105.3) |  |
| Game_Systems / Player / Camera_Root_01 / Main_Camera_01 | (64.9, 1.6, -105.3) |  |
| Supermarket / Cold_Room / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Cold_Room / Doors / Partition_Door_Header_01 | (55.0, 5.2, -182.5) |  |
| Supermarket / Cold_Room / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / Cold_Room / Doors_And_Wall_Finishes / White_Frame_Door_01 | (55.0, 0.0, -183.5) |  |
| Supermarket / Cold_Room / Refrigeration / Cold_Storage_Cabinets_01 | (0.0, 0.0, 0.0) |  |
| Supermarket / Cold_Room / Refrigeration / Cold_Storage_Cabinets_01 / Door_Frame_Rail_01 | (71.6, 0.0, -175.8) | HideChiller.prefab |
| Supermarket / Lobby / Cabinets_And_Storage | (0.0, 0.0, 0.0) |  |
| Supermarket / Lobby / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Lobby / Doors / Door_Lining_Header_01 | (42.5, 3.1, -120.0) |  |
| Supermarket / Lobby / Doors / Inner_Door_Mat_01 | (42.5, 0.0, -118.8) | EntryMat.prefab |
| Supermarket / Lobby / Doors / Outer_Door_Mat_01 | (42.5, 0.0, -111.2) | EntryMat.prefab |
| Supermarket / Lobby / Doors / Sign_ENTRANCE_01 | (67.5, 2.9, -119.1) |  |
| Supermarket / Lobby / Doors / Sign_ENTRANCE_01 / Label_ENTRANCE_01 | (67.5, 2.9, -119.1) |  |
| Supermarket / Lobby / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_01 / Paper_Bin_01 | (35.5, 0.0, -110.3) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_01 / Plastic_Bin_01 | (36.0, 0.0, -110.3) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_01 / Waste_Bin_01 | (35.0, 0.0, -110.3) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_02 / Paper_Bin_01 | (51.0, 0.0, -119.7) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_02 / Plastic_Bin_01 | (50.5, 0.0, -119.6) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_02 / Waste_Bin_01 | (51.5, 0.0, -119.7) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_03 / Paper_Bin_01 | (71.5, 0.0, -119.7) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_03 / Plastic_Bin_01 | (71.0, 0.0, -119.6) | RecyclingBin.prefab |
| Supermarket / Lobby / Recycling / Lobby_Furnished_Recycling_03 / Waste_Bin_01 | (72.0, 0.0, -119.7) | RecyclingBin.prefab |
| Supermarket / Lobby / Signs_And_Notices / Sign_EXIT_01 | (42.5, 2.9, -119.1) |  |
| Supermarket / Lobby / Signs_And_Notices / Sign_EXIT_01 / Label_EXIT_01 | (42.5, 2.9, -119.1) |  |
| Supermarket / Office / Cabinets_And_Storage | (0.0, 0.0, 0.0) |  |
| Supermarket / Office / Cabinets_And_Storage / Archive_Cabinet_01 | (20.6, 0.0, -123.7) | TallCabinet.prefab |
| Supermarket / Office / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Office / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / Office / Furniture_And_Props / Office_Camera_01 | (24.7, 2.9, -123.8) | SecurityCamera.prefab |
| Supermarket / Office / Inactive_Originals / Door_Header_01 | (25.0, 5.3, -130.0) |  |
| Supermarket / Office / Recycling / Filing_Reference_Binders_01 | (10.3, 1.4, -130.1) | Binders.prefab |
| Supermarket / Office / Recycling / Office_Furnished_Recycling_01 / Paper_Bin_01 | (24.6, 0.0, -127.3) | RecyclingBin.prefab |
| Supermarket / Office / Recycling / Office_Furnished_Recycling_01 / Plastic_Bin_01 | (24.6, 0.0, -127.8) | RecyclingBin.prefab |
| Supermarket / Office / Recycling / Office_Furnished_Recycling_01 / Waste_Bin_01 | (24.7, 0.0, -126.8) | RecyclingBin.prefab |
| Supermarket / Office / Recycling / Personnel_Binders_01 | (15.6, 0.9, -136.3) | Binders.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_01 | (31.0, 0.0, -120.4) |  |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_01 / Paper_Bin_01 | (31.0, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_01 / Plastic_Bin_01 | (31.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_01 / Waste_Bin_01 | (30.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_02 / Paper_Bin_01 | (54.0, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_02 / Plastic_Bin_01 | (54.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_02 / Waste_Bin_01 | (53.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_03 / Paper_Bin_01 | (78.0, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_03 / Plastic_Bin_01 | (78.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Architecture / Sales_Floor_Bin_03 / Waste_Bin_01 | (77.5, 0.0, -120.4) | RecyclingBin.prefab |
| Supermarket / Sales_Floor / Checkouts | (0.0, 0.0, 0.0) |  |
| Supermarket / Sales_Floor / Checkouts / Checkout_Collision_Models_01 | (53.5, 0.0, -153.0) |  |
| Supermarket / Sales_Floor / Checkouts / Checkout_Collision_Models_01 / Cashier_01 | (29.9, 1.0, -131.5) |  |
| Supermarket / Sales_Floor / Checkouts / Checkout_Impulse_Shelf_01 | (29.5, 0.0, -132.5) | Checkout.prefab |
| Supermarket / Sales_Floor / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Sales_Floor / Doors / Sales_Entrance_Greenery_01 | (28.0, 0.0, -120.4) | LobbyPlanter.prefab |
| Supermarket / Sales_Floor / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / Sales_Floor / Doors_And_Wall_Finishes / White_Frame_Door_01 | (43.5, 0.0, -175.0) |  |
| Supermarket / Sales_Floor / Refrigeration / Retail_Chillers_01 | (0.0, 0.0, 0.0) |  |
| Supermarket / Sales_Floor / Signs_And_Notices / Sign_CHECKOUT_01 | (29.9, 2.8, -130.5) |  |
| Supermarket / Shared_Building / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Shared_Building / Doors / Automatic_Doors_01 | (20.0, 0.0, -124.9) |  |
| Supermarket / Shared_Building / Doors / Automatic_Doors_01 / Right_Door_01 | (42.5, 0.0, -110.0) | AutoDoubleDoor.prefab |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 | (20.0, 0.0, -124.9) |  |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 / Door_01 | (25.0, 0.0, -123.5) | SingleDoor.prefab |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 / Door_03 / Finished_Door_Leaf_01 | (42.5, 1.2, -175.0) |  |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 / Door_04 / Finished_Door_Leaf_01 | (17.5, 1.2, -175.0) |  |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 / Door_05 / Finished_Door_Leaf_01 | (67.5, 1.2, -175.0) |  |
| Supermarket / Shared_Building / Doors / Hinged_Doors_01 / Door_06 / Finished_Door_Leaf_01 | (55.0, 1.2, -182.5) |  |
| Supermarket / Shared_Building / Lighting_And_Audio / Mall_Speakers_01 | (0.0, 0.0, 0.0) |  |
| Supermarket / Shared_Building / Lighting_And_Audio / Mall_Speakers_01 / Speaker_12_5_117_01 | (12.5, 6.6, -117.5) |  |
| Supermarket / Staff_Room / Cabinets_And_Storage | (0.0, 0.0, 0.0) |  |
| Supermarket / Staff_Room / Cabinets_And_Storage / Changing_Cabin_01 | (11.5, 0.0, -142.0) | ChangingCabin.prefab |
| Supermarket / Staff_Room / Cabinets_And_Storage / Hinge_Locker_01 | (16.8, 0.0, -137.4) | Locker.prefab |
| Supermarket / Staff_Room / Doors | (0.0, 0.0, 0.0) |  |
| Supermarket / Staff_Room / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / Staff_Room / Doors_And_Wall_Finishes / White_Frame_Door_01 | (18.5, 0.0, -175.0) |  |
| Supermarket / Staff_Room / Furniture_And_Props / Staff_Fridge_01 | (10.7, 0.0, -149.4) | Fridge.prefab |
| Supermarket / Staff_Room / Gameplay_Tools / Coffee_Machine_01 | (10.6, 1.0, -148.0) |  |
| Supermarket / Staff_Room / Gameplay_Tools / Flashlight_01 | (15.5, 1.0, -146.4) |  |
| Supermarket / Staff_Room / Gameplay_Tools / Mop_01 | (11.2, 1.0, -149.7) |  |
| Supermarket / Staff_Room / Gameplay_Tools / Staff_Radio_01 | (10.3, 1.7, -146.7) |  |
| Supermarket / Staff_Room / Gameplay_Tools / Time_Clock_01 | (24.9, 1.2, -139.3) |  |
| Supermarket / Staff_Room / Gameplay_Tools / Tool_Snap_Points_01 / Mop_Snap_Point_01 | (11.2, 1.0, -149.7) |  |
| Supermarket / Staff_Room / Inactive_Originals / East_Doorway_Top_01 | (24.9, 5.2, -142.5) |  |
| Supermarket / Staff_Room / Inactive_Originals / Entrance_Infill_01 | (24.9, 1.2, -143.4) |  |
| Supermarket / Staff_Room / Recycling / Staff_Room_Furnished_Recycling_01 / Paper_Bin_01 | (14.8, 0.0, -149.7) | RecyclingBin.prefab |
| Supermarket / Staff_Room / Recycling / Staff_Room_Furnished_Recycling_01 / Plastic_Bin_01 | (14.3, 0.0, -149.7) | RecyclingBin.prefab |
| Supermarket / Staff_Room / Recycling / Staff_Room_Furnished_Recycling_01 / Waste_Bin_01 | (15.3, 0.0, -149.7) | RecyclingBin.prefab |
| Supermarket / Storage / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / WC / Cabinets_And_Storage | (0.0, 0.0, 0.0) |  |
| Supermarket / WC / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Cleaning_Mop_01 | (24.6, 1.1, -120.5) |  |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Mop_Home_01 | (24.6, 1.1, -120.5) |  |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_East_01 / Paper_Bin_01 | (19.5, 0.0, -116.7) | RecyclingBin.prefab |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_East_01 / Plastic_Bin_01 | (19.5, 0.0, -116.1) | RecyclingBin.prefab |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_East_01 / Waste_Bin_01 | (19.5, 0.0, -117.2) | RecyclingBin.prefab |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_West_01 / Paper_Bin_01 | (15.2, 0.0, -116.7) | RecyclingBin.prefab |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_West_01 / Plastic_Bin_01 | (15.2, 0.0, -117.2) | RecyclingBin.prefab |
| Supermarket / WC / Furniture_And_Props / WC_Finishing_01 / WC_Recycling_West_01 / Waste_Bin_01 | (15.2, 0.0, -116.1) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Door_Aligned_Raised_Crossing_01 | (42.5, 0.0, -101.5) |  |
| Exterior / Parking / Roads_And_Furniture / Entrance_Pedestrian_Apron_01 | (50.0, 0.1, -107.8) |  |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_01 / Paper_Bin_01 | (31.5, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_01 / Plastic_Bin_01 | (32.0, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_01 / Waste_Bin_01 | (31.0, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_02 / Paper_Bin_01 | (86.5, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_02 / Plastic_Bin_01 | (87.0, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Parking_Recycling_02 / Waste_Bin_01 | (86.0, 0.1, -75.5) | RecyclingBin.prefab |
| Exterior / Parking / Roads_And_Furniture / Protected_Entrance_Walkway_01 | (42.5, 0.1, -81.2) |  |
| Exterior / Service_Yard / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Exterior / Service_Yard / Utilities / Backstreet_01 / Container_01 / Trash_Drop_Zone_01 | (22.6, 0.9, -164.5) |  |
| Exterior / Service_Yard / Utilities / Backstreet_01 / Trashcan_01 | (39.3, 0.0, -109.1) | Trashcan.prefab |
| Exterior / Storefront / Doors | (0.0, 0.0, 0.0) |  |
| Exterior / Storefront / Doors / Entrance_Canopy_01 | (55.0, 4.2, -107.2) |  |
| Exterior / Storefront / Doors / Sign_ENTRANCE_01 | (67.5, 3.2, -109.7) |  |
| Exterior / Storefront / Doors_And_Wall_Finishes | (0.0, 0.0, 0.0) |  |
| Exterior / Storefront / Recycling / Front_Bin_East_01 | (73.7, 0.0, -109.5) |  |
| Exterior / Storefront / Recycling / Front_Bin_East_01 / Paper_Bin_01 | (73.7, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Recycling / Front_Bin_East_01 / Plastic_Bin_01 | (73.2, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Recycling / Front_Bin_East_01 / Waste_Bin_01 | (74.2, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Recycling / Front_Bin_West_01 | (35.0, 0.0, -109.5) |  |
| Exterior / Storefront / Recycling / Front_Bin_West_01 / Paper_Bin_01 | (35.0, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Recycling / Front_Bin_West_01 / Plastic_Bin_01 | (34.5, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Recycling / Front_Bin_West_01 / Waste_Bin_01 | (35.5, 0.0, -109.5) | RecyclingBin.prefab |
| Exterior / Storefront / Signs_And_Notices / Sign_EXIT_01 | (42.5, 3.2, -109.7) |  |
| Authoring_References / Inactive_Prototypes / Trashcan_01 | (38.9, 0.0, -127.0) | Trashcan.prefab |
| Authoring_References / Legacy_Fixtures / Legacy_Refrigeration_Source_01 / Legacy_Fridge_Type01_01 | (69.5, 0.0, -189.6) | Prop_Fridge_01.prefab |
| Models_Island / Catalogue_Labels_01 / Label_2_M_Player_Height_01 | (0.0, 0.0, 108.0) |  |
| Models_Island / Catalogue_Labels_01 / Label_Bag_Lockers_3_6_0_66_2_4_M_01 | (0.0, 0.0, 70.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Binders_0_59_0_29_0_34_M_01 | (0.0, 0.0, 60.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Changing_Cabin_2_6_2_78_2_6_M_01 | (0.0, 0.0, 4.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Checkout_2_61_5_1_67_M_01 | (0.0, 0.0, 90.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Cleaning_Hide_Cabinet_1_5_1_54_2_5_M_01 | (0.0, 0.0, 54.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Coffee_Machine_0_48_0_73_0_62_M_01 | (0.0, 0.0, 10.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Coffee_Table_1_6_0_75_0_43_M_01 | (0.0, 0.0, 16.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Filing_Cabinet_0_85_0_73_1_36_M_01 | (0.0, 0.0, 48.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Flashlight_0_33_0_14_0_15_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Fridge_1_05_1_13_2_1_M_01 | (0.0, 0.0, 10.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Hide_Chiller_1_8_1_53_2_6_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Locker_1_2_1_53_2_4_M_01 | (0.0, 0.0, 4.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Locker_Open_Interior_1_54_2_48_2_4_M_01 | (0.0, 0.0, 34.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Mop_0_48_0_22_1_94_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Outdoor_Waste_Container_2_2_1_44_1_45_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Printer_Cabinet_1_6_0_74_0_86_M_01 | (0.0, 0.0, 54.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Public_Bin_0_7_0_65_1_04_M_01 | (0.0, 0.0, 90.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Radio_0_7_0_31_0_79_M_01 | (0.0, 0.0, 10.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Recycling_Bin_0_5_0_62_1_04_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Retail_Chiller_1_8_1_33_2_6_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Room_Door_1_66_0_25_2_6_M_01 | (0.0, 0.0, 34.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Sanitary_Bin_0_25_0_42_0_6_M_01 | (0.0, 0.0, 54.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Security_Camera_0_22_0_52_0_21_M_01 | (0.0, 0.0, 66.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Storage_Chiller_1_8_1_53_2_6_M_01 | (0.0, 0.0, 96.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Tall_Cabinet_1_2_0_68_2_2_M_01 | (0.0, 0.0, 48.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Time_Clock_0_38_0_16_0_5_M_01 | (0.0, 0.0, 10.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Toilet_Cabin_2_65_3_48_2_7_M_01 | (0.0, 0.0, 48.7) |  |
| Models_Island / Catalogue_Labels_01 / Label_Toilet_Cabin_Open_Interior_2_65_3_4_2_7_M_01 | (0.0, 0.0, 60.7) |  |
| … 73 more | | |

## Most-used prefabs

| prefab | instances |
|---|---|
| `Wall.prefab` | 202 |
| `ShelfTwoside_3_grey_Ready.prefab` | 72 |
| `Prop_Fridge_01.prefab` | 60 |
| `StorageChiller.prefab` | 46 |
| `RecyclingBin.prefab` | 42 |
| `Shelfpillar_I_3_grey_Ready.prefab` | 32 |
| `StorageRack_02.dae` | 28 |
| `RetailChiller.prefab` | 26 |
| `Shelfpillar_L_3_grey_Ready.prefab` | 22 |
| `Shelfpillar_b_3_grey_Ready.prefab` | 19 |
| `ParkingLamp.prefab` | 18 |
| `DiningChair.prefab` | 18 |
| `Prop_Fridge_04.prefab` | 17 |
| `Trolley.prefab` | 16 |
| `Prop_Fridge_03.prefab` | 16 |
| `ShelfOneside_3_grey_Ready.prefab` | 12 |
| `Bollard.prefab` | 12 |
| `LobbyPlanter.prefab` | 12 |
| `CarboardBox_1.prefab` | 11 |
| `Locker.prefab` | 10 |
| `ToiletCabin.prefab` | 10 |
| `ParkingTree.prefab` | 9 |
| `SecurityGate.prefab` | 8 |
| `HideChiller.prefab` | 8 |
| `Downpipe.prefab` | 8 |
