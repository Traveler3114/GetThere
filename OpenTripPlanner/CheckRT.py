import requests
from google.transit import gtfs_realtime_pb2

# Ask user for URL
url = input("Enter GTFS-RT protobuf URL: ").strip()

try:
    response = requests.get(url)
    response.raise_for_status()

    feed = gtfs_realtime_pb2.FeedMessage()
    feed.ParseFromString(response.content)

    print("\n--- Showing first 5 trip updates ---\n")

    for entity in feed.entity[:5]:
        if entity.HasField('trip_update'):
            print('RT trip_id:', entity.trip_update.trip.trip_id)
            print('RT route_id:', entity.trip_update.trip.route_id)
            print('RT start_date:', entity.trip_update.trip.start_date)
            print('---')

except Exception as e:
    print("Error:", e)

# Pause so window doesn't close
input("\nPress Enter to exit...")